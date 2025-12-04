using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Minorag.Cli.Models.Domain;

namespace Minorag.Cli.Store;

public class RagDbContext(DbContextOptions<RagDbContext> options) : DbContext(options)
{
    public DbSet<CodeChunk> Chunks => Set<CodeChunk>();
    public DbSet<Repository> Repositories => Set<Repository>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var repo = modelBuilder.Entity<Repository>();

        repo.ToTable("repositories");

        repo.HasKey(r => r.Id);

        repo.Property(r => r.RootPath)
            .IsRequired()
            .HasColumnName("root_path");

        repo.HasIndex(r => r.RootPath)
            .IsUnique();

        repo.Property(r => r.Name)
            .IsRequired()
            .HasColumnName("name");

        var chunk = modelBuilder.Entity<CodeChunk>();

        chunk.ToTable("chunks");

        chunk.HasKey(c => c.Id);
        chunk.HasOne(c => c.Repository)
                 .WithMany(r => r.Chunks)
                 .HasForeignKey(c => c.RepositoryId)
                 .OnDelete(DeleteBehavior.Cascade);
        chunk.Property(c => c.Path)
            .IsRequired()
            .HasColumnName("path");

        chunk.Property(c => c.Extension)
            .IsRequired()
            .HasColumnName("extension");

        chunk.Property(c => c.Language)
            .IsRequired()
            .HasColumnName("language");

        chunk.Property(c => c.Kind)
            .IsRequired()
            .HasColumnName("kind");

        chunk.Property(c => c.SymbolName)
            .HasColumnName("symbol_name");

        chunk.Property(c => c.Content)
            .IsRequired()
            .HasColumnName("content");

        chunk.Property(c => c.FileHash)
            .IsRequired()
            .HasColumnName("file_hash");

        chunk.Property(c => c.ChunkIndex)
            .HasColumnName("chunk_index");

        var embeddingConverter = new ValueConverter<float[], byte[]>(
            v => FloatArrayToBytes(v),
            v => BytesToFloatArray(v));

        // Expression-bodied lambdas only (no statements) → works with expression trees
        var embeddingComparer = new ValueComparer<float[]>(
            // equality: reference equal OR same length + SequenceEqual
            (a, b) =>
                a == b ||
                (a != null && b != null && a.Length == b.Length && a.SequenceEqual(b)),
            // hash: simple, but valid expression; good enough for change tracking
            v => v.Length,
            // snapshot: clone array
            v => v.ToArray());

        var embeddingProperty = chunk.Property(c => c.Embedding)
            .HasConversion(embeddingConverter)
            .HasColumnName("embedding")
            .IsRequired();

        embeddingProperty.Metadata.SetValueComparer(embeddingComparer);

        chunk.HasIndex(c => c.Path).HasDatabaseName("idx_chunks_path");
        chunk.HasIndex(c => c.Extension).HasDatabaseName("idx_chunks_extension");
        chunk.HasIndex(c => new { c.RepositoryId, c.Path }).HasDatabaseName("idx_chunks_repo_path");
    }

    private static byte[] FloatArrayToBytes(float[] array)
    {
        if (array.Length == 0)
        {
            return [];
        }

        var bytes = new byte[array.Length * sizeof(float)];
        Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToFloatArray(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return [];
        }

        var array = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, array, 0, bytes.Length);
        return array;
    }
}