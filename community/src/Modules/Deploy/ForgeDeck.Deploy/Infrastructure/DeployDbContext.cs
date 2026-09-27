using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeDeck.Deploy.Infrastructure;

public sealed class DeployDbContext(DbContextOptions<DeployDbContext> options) : DbContext(options)
{
    public DbSet<DeployEnvironmentRow> Environments => Set<DeployEnvironmentRow>();
    public DbSet<DeployDeploymentRow> Deployments => Set<DeployDeploymentRow>();

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
