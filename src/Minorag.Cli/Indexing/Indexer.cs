using Minorag.Core.Indexing;
using Minorag.Core.Models.Domain;
using Minorag.Core.Models.Indexing;
using Minorag.Core.Providers;
using Minorag.Core.Services;
using Minorag.Core.Store;
using Spectre.Console;

namespace Minorag.Cli.Indexing;

public interface IIndexer
{
    Task IndexAsync(
        string rootPath,
        bool reindex,
        string[] excludePatterns,
        CancellationToken ct);
}

public class Indexer(
    ISqliteStore store,
    IMinoragConsole console,
    IEmbeddingProvider provider,
    IRepositoryFilesProvider filesProvider,
    IChunkHelper chunkHelper) : IIndexer
{
    public async Task IndexAsync(
            string rootPath,
            bool reindex,
            string[] excludePatterns,
            CancellationToken ct)
    {
        // Normalize path
        rootPath = Path.GetFullPath(rootPath);

        var repository = await store.GetOrCreateRepositoryAsync(rootPath, ct);

        console.WriteMarkupLine(
            "[cyan]Loading existing file hashes for repo:[/] [blue]{0}[/]",
            console.EscapeMarkup(repository.RootPath));

        var existingHashes = await store.GetFileHashesAsync(repository.Id, ct);

        var (allFiles, ignoredFilesCount, ignoredDirsCount) = await filesProvider.GetAllFiles(rootPath, excludePatterns, ct);

        console.WriteMarkupLine(
            "[cyan]Found[/] [green]{0}[/] [cyan]candidate files to index (after built-in + ignore filtering).[/]",
            allFiles.Count);

        var totalIgnoredFiles = ignoredFilesCount + ignoredDirsCount;
        if (totalIgnoredFiles > 0)
        {
            console.WriteMarkupLine(
                "[cyan]Ignored[/] [yellow]{0}[/] [cyan]paths via [/] [yellow].minoragignore [/] / [yellow] --exclude [/] [cyan](files: {1}, dirs: {2}).[/]",
                totalIgnoredFiles,
                ignoredFilesCount,
                ignoredDirsCount);
        }

        var indexedCount = 0;
        var unchanged = 0;

        // Progress bar for indexing
        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[white]Indexing files[/]", maxValue: allFiles.Count);

                foreach (var file in allFiles)
                {
                    ct.ThrowIfCancellationRequested();

                    var fileName = Path.GetFileName(file);

                    var ext = chunkHelper.GetExtension(file);

                    var relPath = Path.GetRelativePath(rootPath, file);

                    var content = await filesProvider.ReadFileAsync(file, ct);

                    if (existingHashes.TryGetValue(relPath, out string? oldHash) && !reindex &&
                        string.Equals(oldHash, content.Hash, StringComparison.Ordinal))
                    {
                        unchanged++;
                        task.Increment(1);
                        continue;
                    }

                    PrintFileInfo(reindex, relPath, oldHash);
                    await ProcessFile(repository, ext, relPath, content, ct);

                    indexedCount++;
                    task.Increment(1);
                }
            });

        await store.SetRepositoryLastIndexDate(repository.Id, ct);

        console.WriteMarkupLine(
            "[cyan]Indexing completed.[/] " +
            "[green]{0}[/] [green]files indexed[/], " +
            "[dim]⚪ {1} unchanged[/], " +
            "[yellow]{2}[/] [yellow]paths skipped[/].",
            indexedCount,
            unchanged,
            totalIgnoredFiles);
    }

    private async Task ProcessFile(Repository repository, string ext, string relPath, FileContent content, CancellationToken ct)
    {
        await store.DeleteChunksForFileAsync(repository.Id, relPath, ct);

        var chunkIndex = 0;

        var spec = chunkHelper.ChooseChunkSpec(relPath, ext, content.Content);

        var chunks = chunkHelper.ChunkContentByTokens(
            content.Content,
            spec.MaxTokens,
            spec.OverlapTokens,
            spec.HardMaxChars,
            spec.Mode).ToArray();

        if (chunks.Length > 12)
        {
            console.WriteWarning($"File consists of {chunks.Length} chunks. Review the file for single responsibility or duplicated code.");
        }

        foreach (var chunkContent in chunks)
        {
            ct.ThrowIfCancellationRequested();

            if (chunks.Length > 1)
            {
                console.WriteMarkupLine($"  [green]➕ Indexing chunk {chunkIndex + 1} of {chunks.Length}: [/] Length: {chunkContent.Length}");
            }

            var chunk = new CodeChunk
            {
                RepositoryId = repository.Id,
                Path = relPath,
                Extension = ext,
                Language = chunkHelper.GuessLanguage(ext),
                Kind = "file",
                SymbolName = null,
                Content = chunkContent,
                FileHash = content.Hash,
                ChunkIndex = chunkIndex++
            };

            try
            {
                chunk.Embedding = await provider.EmbedAsync(chunk.Content, ct);
            }
            catch (Exception ex)
            {
                var error = $"[yellow bold] ⚠️ Failed to embed[/] [cyan]{console.EscapeMarkup(chunk.Path)}[/]: [red]{console.EscapeMarkup(ex.Message)} [/]";
                console.WriteMarkupLine(error);

                await store.DeleteChunksForFileAsync(repository.Id, relPath, ct);

                break;
            }

            await store.InsertChunkAsync(chunk, ct);
        }
    }

    private void PrintFileInfo(bool reindex, string relPath, string? oldHash)
    {
        if (oldHash is null)
        {
            console.WriteMarkupLine(
                "[green]➕ New file, indexing:[/] [cyan]{0}[/]",
                console.EscapeMarkup(relPath));
        }
        else if (reindex)
        {
            console.WriteMarkupLine(
                "[yellow]♻ Re-index applied, re-indexing:[/] [cyan]{0}[/]",
                console.EscapeMarkup(relPath));
        }
        else
        {
            console.WriteMarkupLine(
                "[yellow]♻ Changed file, re-indexing:[/] [cyan]{0}[/]",
                console.EscapeMarkup(relPath));
        }
    }
}