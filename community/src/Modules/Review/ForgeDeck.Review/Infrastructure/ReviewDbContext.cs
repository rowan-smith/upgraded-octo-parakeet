using ForgeDeck.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Review.Infrastructure;

public sealed class ReviewDbContext(DbContextOptions<ReviewDbContext> options) : DbContext(options)
{
    public DbSet<ChangeRow> Changes => Set<ChangeRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<string>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChangeRow>(entity =>
        {
            entity.ToTable("review_changes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.ProviderId).HasColumnName("provider_id").IsRequired();
            entity.Property(e => e.Owner).HasColumnName("owner").IsRequired();
            entity.Property(e => e.RepositoryName).HasColumnName("repository_name").IsRequired();
            entity.Property(e => e.ExternalId).HasColumnName("external_id").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.Property(e => e.Payload).HasColumnName("payload").IsRequired();
            entity.HasIndex(e => new { e.ProviderId, e.Owner, e.RepositoryName, e.ExternalId }).IsUnique();
            entity.HasIndex(e => new { e.ProjectId, e.UpdatedAt }).HasDatabaseName("ix_review_changes_project");
        });
    }

    public void EnsureSchema()
    {
        var table = Model.GetEntityTypes().Select(e => e.GetTableName()).First(t => t != null)!;
        SchemaBootstrap.EnsureRelationalTables(this, table);
        Database.OpenConnection();
        SchemaBootstrap.Record(
            Database.GetDbConnection(),
            "review",
            SchemaBootstrap.PlatformSchemaVersion);
    }
}

public sealed class ChangeRow
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public string Owner { get; set; } = "";
    public string RepositoryName { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string Status { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string Payload { get; set; } = "";
}
