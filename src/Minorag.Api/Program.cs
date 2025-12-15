using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Minorag.Api;
using Minorag.Api.Extensions;
using Minorag.Api.Models;
using Minorag.Core.Configuration;
using Minorag.Core.Models.Options;
using Minorag.Core.Services;
using Minorag.Core.Services.Environments;
using Minorag.Core.Store;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "MINORAG_");

var dbPath =
    builder.Configuration["Database:Path"]
    ?? RagEnvironment.GetDefaultDbPath();

// IMPORTANT: don’t use "~" in containers; if you still allow it locally, expand it here
if (dbPath.StartsWith("~/"))
{
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    dbPath = Path.Combine(home, dbPath[2..]);
}

Console.WriteLine("DB:" + dbPath);

builder.Services.RegisterDatabase(dbPath);

builder.Services.SetupConfiguration(builder.Configuration);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.AddDebug();
builder.Services.AddHttpLogging(o =>
{
    o.LoggingFields =
        Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestMethod |
        Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestPath |
        Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.ResponseStatusCode;
});

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Minorag.Api",
        Version = "v1"
    });
});


builder.Services.RegisterServices();
builder.Services.AddSingleton<IMinoragConsole, NoOpConsole>();

var app = builder.Build();
app.UseHttpLogging();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/swagger/v1/swagger.json", "Minorag.Api v1");
        o.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();

app.MapPatch("/doctor", async (
    HttpContext http,
    ILogger<Program> logger,
    IEnvironmentDoctor doctor,
    IOptions<DatabaseOptions> options,
    CancellationToken ct) =>
{
    var workingDirectory = Directory.GetCurrentDirectory();

    http.Response.StatusCode = StatusCodes.Status200OK;
    http.Response.ContentType = "application/x-ndjson; charset=utf-8";

    await foreach (var result in doctor.DiagnoseAsync(dbPath, workingDirectory, ct)
                                      .WithCancellation(ct))
    {
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        var line = Encoding.UTF8.GetBytes(json + "\n");

        await http.Response.Body.WriteAsync(line, ct);
        await http.Response.Body.FlushAsync(ct);
    }
});

app.MapPost("/ask", async Task<IResult> (
    HttpContext http,
    ScopeResolver scopeResolver,
    ISearcher searcher,
    IOptions<RagOptions> ragOptions,
    [FromBody] AskRequest request,
    CancellationToken ct) =>
{
    var repositories = await scopeResolver.ResolveScopeAsync(
        currentDirectory: request.CurrentDirectory ?? "~/",
        repoNames: request.ExplicitRepoNames ?? [],
        reposCsv: request.ReposCsv,
        projectName: request.ProjectName,
        clientName: request.ClientName,
        request.AllRepos,
        ct);

    var repoIds = repositories.Select(r => r.Id).ToList();

    // 2) Retrieve context
    var effectiveTopK = request.TopK ?? ragOptions.Value.TopK;

    var context = await searcher.RetrieveAsync(
        request.Question ?? "",
        request.Verbose,
        repositoryIds: repoIds,
        topK: effectiveTopK,
        ct: ct);

    context.UseAdvancedModel = request.UseAdvancedModel;

    // 3) If no-LLM or no results -> return JSON SearchResult
    if (request.NoLlm || !context.HasResults)
    {
        return Results.Ok(new Minorag.Core.Models.SearchResult(context.Question, context.Chunks, Answer: null));
    }

    // 4) Otherwise stream the LLM output
    // Assumption: AnswerStreamAsync yields text chunks/tokens.
    // If it yields something else (e.g., objects), tell me the type and I’ll adapt.
    var tokenStream = searcher.AnswerStreamAsync(
        context,
        useLlm: true,
        memorySummary: null,
        ct: ct);

    return Results.Stream(
        async stream =>
        {
            await stream.WriteLine("|Files|Score|", ct);
            await stream.WriteLine("|----|-----|", ct);

            foreach (var c in context.Chunks)
            {
                var score = c.Score.ToString("0.000"); // nicer
                await stream.WriteLine($"|`{c.Chunk.Path}`|{score}|", ct);
            }

            await stream.WriteLine("", ct);

            await foreach (var tok in tokenStream.WithCancellation(ct))
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(tok?.ToString() ?? ""), ct);
                await stream.FlushAsync(ct);
            }
        },
        contentType: "text/plain; charset=utf-8"
    );
});

app.MapGet("/repos", async (ISqliteStore store, CancellationToken ct) =>
{
    // Fetch all repositories from the SQLite store
    var repos = await store.GetRepositories(ct);
    return Results.Ok(repos);   // Spectre.NET minimal API will serialize to JSON
});

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
