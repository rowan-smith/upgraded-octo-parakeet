using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeDeck.Build.Infrastructure;

public sealed class BuildDbContext(DbContextOptions<BuildDbContext> options) : DbContext(options)
{
    public DbSet<PipelineDefinitionRow> Definitions => Set<PipelineDefinitionRow>();
    public DbSet<PipelineDefinitionVersionRow> DefinitionVersions => Set<PipelineDefinitionVersionRow>();
    public DbSet<PipelineRunRow> Runs => Set<PipelineRunRow>();
    public DbSet<PipelineRunnerRow> Runners => Set<PipelineRunnerRow>();
    public DbSet<PipelineRegistrationTokenRow> RegistrationTokens => Set<PipelineRegistrationTokenRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<string>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PipelineDefinitionRow>(entity =>
        {
            entity.ToTable("pipelines_definitions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Enabled).HasColumnName("enabled");
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.Payload).HasColumnName("payload").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.HasIndex(e => new { e.ProjectId, e.Name }).HasDatabaseName("ix_pipelines_definitions_project");
        });

        modelBuilder.Entity<PipelineDefinitionVersionRow>(entity =>
        {
            entity.ToTable("pipelines_definition_versions");
            entity.HasKey(e => new { e.DefinitionId, e.Version });
            entity.Property(e => e.DefinitionId).HasColumnName("definition_id");
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.Payload).HasColumnName("payload").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        });

        modelBuilder.Entity<PipelineRunRow>(entity =>
        {
            entity.ToTable("pipelines_runs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DefinitionId).HasColumnName("definition_id").IsRequired();
            entity.Property(e => e.ChangeId).HasColumnName("change_id");
            entity.Property(e => e.CommitSha).HasColumnName("commit_sha").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.Payload).HasColumnName("payload").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.HasIndex(e => new { e.ChangeId, e.CreatedAt }).HasDatabaseName("ix_pipelines_runs_change");
            entity.HasIndex(e => new { e.DefinitionId, e.CreatedAt }).HasDatabaseName("ix_pipelines_runs_definition");
            entity.HasIndex(e => e.Status).HasDatabaseName("ix_pipelines_runs_status");
        });

        modelBuilder.Entity<PipelineRunnerRow>(entity =>
        {
            entity.ToTable("pipelines_runners");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.TokenHash).HasColumnName("token_hash").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.Payload).HasColumnName("payload").IsRequired();
            entity.Property(e => e.LastHeartbeat).HasColumnName("last_heartbeat");
            entity.HasIndex(e => e.TokenHash).IsUnique();
        });

        modelBuilder.Entity<PipelineRegistrationTokenRow>(entity =>
        {
            entity.ToTable("pipelines_registration_tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TokenHash).HasColumnName("token_hash").IsRequired();
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();
            entity.Property(e => e.ConsumedAt).HasColumnName("consumed_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.HasIndex(e => e.TokenHash).IsUnique();
        });
    }

    public void EnsureSchema()
    {
        Database.OpenConnection();
        var conn = Database.GetDbConnection();
        using var cmd = conn.CreateCommand();
        var table = Model.GetEntityTypes().Select(e => e.GetTableName()).First(t => t != null);
        cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'";
        var exists = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        if (!exists)
        {
            var creator = (IRelationalDatabaseCreator)Database.GetService(typeof(IRelationalDatabaseCreator));
            creator.CreateTables();
        }
    }
}

public sealed class PipelineDefinitionRow
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Enabled { get; set; }
    public int Version { get; set; }
    public string Payload { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public sealed class PipelineDefinitionVersionRow
{
    public string DefinitionId { get; set; } = "";
    public int Version { get; set; }
    public string Payload { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public sealed class PipelineRunRow
{
    public string Id { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public string? ChangeId { get; set; }
    public string CommitSha { get; set; } = "";
    public string Status { get; set; } = "";
    public string Payload { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public sealed class PipelineRunnerRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public string Status { get; set; } = "";
    public string Payload { get; set; } = "";
    public string? LastHeartbeat { get; set; }
}

public sealed class PipelineRegistrationTokenRow
{
    public string Id { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
    public string? ConsumedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}
