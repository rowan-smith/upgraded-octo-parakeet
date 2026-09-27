using ForgeDeck.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Git.Infrastructure;

public sealed class GitDbContext(DbContextOptions<GitDbContext> options) : DbContext(options)
{
    public DbSet<GitRepositoryRow> Repositories => Set<GitRepositoryRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<string>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GitRepositoryRow>(entity =>
        {
            entity.ToTable("git_repositories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Slug).HasColumnName("slug").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Payload).HasColumnName("payload").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.HasIndex(e => e.Slug).IsUnique().HasDatabaseName("ix_git_repositories_slug");
        });
    }

    public void EnsureSchema()
    {
        var table = Model.GetEntityTypes().Select(e => e.GetTableName()).First(t => t != null)!;
        SchemaBootstrap.EnsureRelationalTables(this, table);
        Database.OpenConnection();
        SchemaBootstrap.Record(
            Database.GetDbConnection(),
            "git",
            SchemaBootstrap.PlatformSchemaVersion);
    }
}

public sealed class GitRepositoryRow
{
    public string Id { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Payload { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}
