using ForgeDeck.Build.Domain;

namespace ForgeDeck.Build.Serialization;

/// <summary>Premade jobs the pipeline builder offers as one-click building blocks.</summary>
public static class PipelineJobCatalog
{
    public static IReadOnlyList<PipelineJobTemplate> Templates { get; } =
    [
        new(
            "dotnet-build",
            "DotNet Restore+Build",
            "Restore NuGet packages then build the solution in Release.",
            ".NET",
            ".N",
            new PipelineJobDefinition(
                "Build",
                [
                    Step("Restore packages", "dotnet restore", 600),
                    Step("Build solution", "dotnet build --no-restore -c Release", 900)
                ],
                ["dotnet"],
                new Dictionary<string, string> { ["DOTNET_NOLOGO"] = "true" },
                1800,
                PublishCheck: true,
                CheckName: "Build",
                ArtifactGlobs: [])),
        new(
            "dotnet-test",
            "DotNet Test",
            "Run the test suite and publish TRX results as artifacts.",
            ".NET",
            "✓",
            new PipelineJobDefinition(
                "Unit Tests",
                [
                    Step("Restore packages", "dotnet restore", 600),
                    Step("Build solution", "dotnet build --no-restore -c Release", 900),
                    Step(
                        "Run tests",
                        "dotnet test --no-build -c Release --logger \"trx;LogFileName=tests.trx\" --results-directory TestResults",
                        1800)
                ],
                ["dotnet"],
                new Dictionary<string, string> { ["DOTNET_NOLOGO"] = "true" },
                2700,
                PublishCheck: true,
                CheckName: "Unit Tests",
                ArtifactGlobs: ["TestResults/**/*.trx"])),
        new(
            "node-build",
            "Node npm ci + build",
            "Install locked dependencies with npm ci then run the build script.",
            "Node.js",
            "N",
            new PipelineJobDefinition(
                "Node Build",
                [
                    Step("Install dependencies", "npm ci", 900),
                    Step("Build", "npm run build", 900)
                ],
                ["node"],
                new Dictionary<string, string> { ["CI"] = "true" },
                1800,
                PublishCheck: true,
                CheckName: "Node Build",
                ArtifactGlobs: ["dist/**"])),
        new(
            "docker-build",
            "Docker build",
            "Build a container image tagged with the commit SHA.",
            "Containers",
            "D",
            new PipelineJobDefinition(
                "Docker Image",
                [
                    Step("Build image", "docker build -t $FORGEDECK_IMAGE:$FORGEDECK_COMMIT_SHA .", 1800)
                ],
                ["docker"],
                new Dictionary<string, string> { ["DOCKER_BUILDKIT"] = "1" },
                2400,
                PublishCheck: true,
                CheckName: "Docker Image",
                ArtifactGlobs: [])),
        new(
            "shell-script",
            "Shell script",
            "Run an arbitrary shell command — a starting point for custom work.",
            "Custom",
            "$",
            new PipelineJobDefinition(
                "Script",
                [
                    Step("Run script", "echo \"Replace me with your command\"", 600, "bash")
                ],
                [],
                new Dictionary<string, string>(),
                1200,
                PublishCheck: false,
                CheckName: null,
                ArtifactGlobs: [])),
        new(
            "publish-artifacts",
            "Publish artifacts",
            "Publish build output and upload it as a downloadable artifact.",
            "Packaging",
            "⇪",
            new PipelineJobDefinition(
                "Publish",
                [
                    Step("Publish", "dotnet publish -c Release -o artifacts", 1200)
                ],
                ["dotnet"],
                new Dictionary<string, string>(),
                1800,
                PublishCheck: false,
                CheckName: null,
                ArtifactGlobs: ["artifacts/**"]))
    ];

    public static PipelineJobTemplate? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : Templates.FirstOrDefault(template => template.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));

    private static PipelineStepDefinition Step(string name, string command, int timeoutSeconds, string? shell = null) =>
        new(name, command, shell, new Dictionary<string, string>(), timeoutSeconds);
}

public sealed record PipelineJobTemplate(
    string Id,
    string Name,
    string Description,
    string Category,
    string Icon,
    PipelineJobDefinition Job);
