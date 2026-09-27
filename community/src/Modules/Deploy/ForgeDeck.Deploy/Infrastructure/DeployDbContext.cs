using System.Data.Common;
using ForgeDeck.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Deploy.Infrastructure;

public sealed class DeployDbContext(DbContextOptions<DeployDbContext> options) : DbContext(options)
{
    public DbSet<DeployEnvironmentRow> Environments => Set<DeployEnvironmentRow>();
    public DbSet<DeployDeploymentRow> Deployments => Set<DeployDeploymentRow>();
    public DbSet<DeployBuildRunRow> BuildRuns => Set<DeployBuildRunRow>();
    public DbSet<DeployArtifactRow> Artifacts => Set<DeployArtifactRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<string>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeployEnvironmentRow>(entity =>
        {
            entity.ToTable("deploy_environments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Payload).HasColumnName("payload").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.HasIndex(e => new { e.ProjectId, e.Name }).HasDatabaseName("ix_deploy_environments_project");
        });

        modelBuilder.Entity<DeployDeploymentRow>(entity =>
        {
            entity.ToTable("deploy_deployments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.EnvironmentId).HasColumnName("environment_id").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.Payload).HasColumnName("payload").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.HasIndex(e => new { e.EnvironmentId, e.CreatedAt }).HasDatabaseName("ix_deploy_deployments_env");
            entity.HasIndex(e => new { e.ProjectId, e.CreatedAt }).HasDatabaseName("ix_deploy_deployments_project");
        });

        modelBuilder.Entity<DeployBuildRunRow>(entity =>
        {
            entity.ToTable("deploy_build_runs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PipelineName).HasColumnName("pipeline_name").IsRequired();
            entity.Property(e => e.CommitSha).HasColumnName("commit_sha").IsRequired();
            entity.Property(e => e.ChangeId).HasColumnName("change_id");
            entity.Property(e => e.Payload).HasColumnName("payload").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("ix_deploy_build_runs_created");
        });

        modelBuilder.Entity<DeployArtifactRow>(entity =>
        {
            entity.ToTable("deploy_artifacts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RunId).HasColumnName("run_id").IsRequired();
            entity.Property(e => e.PipelineName).HasColumnName("pipeline_name").IsRequired();
            entity.Property(e => e.ArtifactName).HasColumnName("artifact_name").IsRequired();
            entity.Property(e => e.Payload).HasColumnName("payload").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.HasIndex(e => new { e.RunId, e.CreatedAt }).HasDatabaseName("ix_deploy_artifacts_run");
        });
    }

    public void EnsureSchema()
    {
        // Use a stable sentinel so newly added tables do not flip the "empty DB" check.
        SchemaBootstrap.EnsureRelationalTables(this, "deploy_environments");
        Database.OpenConnection();
        var conn = Database.GetDbConnection();
        EnsureTable(conn, """
            CREATE TABLE IF NOT EXISTS "deploy_build_runs" (
                "id" TEXT NOT NULL CONSTRAINT "PK_deploy_build_runs" PRIMARY KEY,
                "pipeline_name" TEXT NOT NULL,
                "commit_sha" TEXT NOT NULL,
                "change_id" TEXT NULL,
                "payload" TEXT NOT NULL,
                "created_at" TEXT NOT NULL
            );
            """);
        EnsureTable(conn, """
            CREATE INDEX IF NOT EXISTS "ix_deploy_build_runs_created" ON "deploy_build_runs" ("created_at");
            """);
        EnsureTable(conn, """
            CREATE TABLE IF NOT EXISTS "deploy_artifacts" (
                "id" TEXT NOT NULL CONSTRAINT "PK_deploy_artifacts" PRIMARY KEY,
                "run_id" TEXT NOT NULL,
                "pipeline_name" TEXT NOT NULL,
                "artifact_name" TEXT NOT NULL,
                "payload" TEXT NOT NULL,
                "created_at" TEXT NOT NULL
            );
            """);
        EnsureTable(conn, """
            CREATE INDEX IF NOT EXISTS "ix_deploy_artifacts_run" ON "deploy_artifacts" ("run_id", "created_at");
            """);

        SchemaBootstrap.Record(conn, "deploy", SchemaBootstrap.PlatformSchemaVersion);
    }

    private static void EnsureTable(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

public sealed class DeployEnvironmentRow
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Payload { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public sealed class DeployDeploymentRow
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string EnvironmentId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Payload { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public sealed class DeployBuildRunRow
{
    public string Id { get; set; } = "";
    public string PipelineName { get; set; } = "";
    public string CommitSha { get; set; } = "";
    public string? ChangeId { get; set; }
    public string Payload { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public sealed class DeployArtifactRow
{
    public string Id { get; set; } = "";
    public string RunId { get; set; } = "";
    public string PipelineName { get; set; } = "";
    public string ArtifactName { get; set; } = "";
    public string Payload { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}
