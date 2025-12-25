using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Minorag.Core.Models.Domain;

namespace Minorag.Core.Store;

public class RagDbContext(DbContextOptions<RagDbContext> options) : DbContext(options)
{
    public DbSet<CodeChunk> Chunks => Set<CodeChunk>();
    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<RepositoryFile> Files => Set<RepositoryFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var client = modelBuilder.Entity<Client>();

        client.ToTable("clients");

        client.Property(x => x.Id).HasColumnName("id");

        client.HasKey(r => r.Id);

        client.Property(r => r.Name)
            .IsRequired()
            .HasColumnName("name");

        client.Property(r => r.Slug)
            .IsRequired()
            .HasColumnName("slug");

        var project = modelBuilder.Entity<Project>();

        project.ToTable("projects");

        project.Property(x => x.Id).HasColumnName("id");

        project.HasKey(r => r.Id);

        project.Property(r => r.Name)
            .IsRequired()
            .HasColumnName("name");

        project.Property(r => r.Slug)
            .IsRequired()
            .HasColumnName("slug");

        project.Property(r => r.ClientId)
            .IsRequired()
            .HasColumnName("client_id");

        project
            .HasOne(x => x.Client)
            .WithMany(x => x.Projects)
            .HasForeignKey(x => x.ClientId);

        var repo = modelBuilder.Entity<Repository>();

        repo.ToTable("repositories");

        repo.HasKey(r => r.Id);

        repo.Property(r => r.RootPath).HasColumnName("id");

        repo.Property(r => r.RootPath)
            .IsRequired()
            .HasColumnName("root_path");

        repo.HasIndex(r => r.RootPath)
            .IsUnique();

        repo.Property(r => r.ProjectId)
                   .HasColumnName("project_id");

        repo.Property(r => r.Name)
            .IsRequired()
            .HasColumnName("name");

        repo.HasOne(x => x.Project)
            .WithMany(x => x.Repositories)
            .HasForeignKey(x => x.ProjectId);

        var file = modelBuilder.Entity<RepositoryFile>();

        file.ToTable("files");

        file.Property(x => x.Id).HasColumnName("id");
        file.Property(x => x.RepositoryId).HasColumnName("repository_id");

        file.HasKey(c => c.Id);
        file.HasOne(c => c.Repository)
                 .WithMany(r => r.Files)
                 .HasForeignKey(c => c.RepositoryId)
                 .OnDelete(DeleteBehavior.Cascade);

        file.Property(c => c.Path)
            .IsRequired()
            .HasColumnName("path");

        file.Property(c => c.Extension)
            .IsRequired()
            .HasColumnName("extension");

        file.Property(c => c.Language)
            .IsRequired()
            .HasColumnName("language");

        file.Property(c => c.Kind)
            .IsRequired()
            .HasColumnName("kind");

        file.Property(c => c.SymbolName)
            .HasColumnName("symbol_name");

        file.Property(c => c.Content)
            .IsRequired()
            .HasColumnName("content");

        file.Property(c => c.FileHash)
            .IsRequired()
            .HasColumnName("file_hash");

        file.HasIndex(c => new { c.RepositoryId, c.Path }).HasDatabaseName("idx_file_repo_path");

        var chunk = modelBuilder.Entity<CodeChunk>();

        chunk.ToTable("chunks");

        chunk.Property(x => x.Id).HasColumnName("id");

        chunk.HasKey(c => c.Id);

        chunk.Property(x => x.FileId).HasColumnName("file_id");

        chunk.HasOne(c => c.File)
                       .WithMany(r => r.Chunks)
                       .HasForeignKey(c => c.FileId)
                       .OnDelete(DeleteBehavior.Cascade);

        chunk.Property(c => c.Content)
            .IsRequired()
            .HasColumnName("content");

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

        chunk.HasIndex(c => new { c.FileId, c.ChunkIndex }).HasDatabaseName("idx_chunks_file_index");
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