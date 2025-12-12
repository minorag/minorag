using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minorag.Cli.Cli;
using Minorag.Cli.Hosting;
using Minorag.Cli.Models.Options;
using Minorag.Cli.Services;
using Minorag.Cli.Services.Chat;
using Spectre.Console;

namespace Minorag.Cli.Commands;

public static class ChatCommandFactory
{
    public static Command Create()
    {
        var cmd = new Command("chat")
        {
            Description = "Interactive mode - like `ask`, but in a loop."
        };

        cmd.Add(CliOptions.TopKOption);
        cmd.Add(CliOptions.RepoNameOption);
        cmd.Add(CliOptions.RepoNamesCsvOption);
        cmd.Add(CliOptions.ClientOption);
        cmd.Add(CliOptions.ProjectOption);
        cmd.Add(CliOptions.DbOption);
        cmd.Add(CliOptions.VerboseOption);
        cmd.Add(CliOptions.NoLlmOption);
        cmd.Add(CliOptions.AllReposOption);
        cmd.Add(CliOptions.DeepOption);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var dbFile = parseResult.GetValue(CliOptions.DbOption);
            var dbPath = dbFile?.FullName ?? RagEnvironment.GetDefaultDbPath();

            var verbose = parseResult.GetValue(CliOptions.VerboseOption);
            var noLlm = parseResult.GetValue(CliOptions.NoLlmOption);
            var useAdvancedModel = parseResult.GetValue(CliOptions.DeepOption);

            using var host = HostFactory.BuildHost(dbPath);
            using var scope = host.Services.CreateScope();

            var ragOptions = scope.ServiceProvider.GetRequiredService<IOptions<RagOptions>>();
            var scopeResolver = scope.ServiceProvider.GetRequiredService<ScopeResolver>();
            var console = scope.ServiceProvider.GetRequiredService<IMinoragConsole>();

            var explicitRepoNames = parseResult.GetValue(CliOptions.RepoNameOption) ?? [];
            var reposCsv = parseResult.GetValue(CliOptions.RepoNamesCsvOption);
            var projectName = parseResult.GetValue(CliOptions.ProjectOption);
            var clientName = parseResult.GetValue(CliOptions.ClientOption);
            var allRepos = parseResult.GetValue(CliOptions.AllReposOption);

            List<int> repoIds;
            try
            {
                var repositories = await scopeResolver.ResolveScopeAsync(
                    Environment.CurrentDirectory,
                    explicitRepoNames,
                    reposCsv,
                    projectName,
                    clientName,
                    allRepos,
                    ct);

                repoIds = [.. repositories.Select(r => r.Id)];
            }
            catch (Exception ex)
            {
                console.WriteError(ex);
                Environment.ExitCode = 1;
                return;
            }

            var effectiveTopK = parseResult.GetValue(CliOptions.TopKOption) ?? ragOptions.Value.TopK;

            var chat = scope.ServiceProvider.GetRequiredService<IChat>();

            await chat.RunLoopAsync(
                repoIds,
                effectiveTopK,
                verbose,
                noLlm,
                useAdvancedModel,
                ct);
        });

        return cmd;
    }
}