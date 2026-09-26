using ForgeDeck.Build.Api;
using ForgeDeck.Build.Domain;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ForgeDeck.Build.Serialization;

/// <summary>Declarative YAML interchange format for pipeline definitions (storage stays JSON).</summary>
public static class PipelineYamlMapper
{
    public const int DefaultPipelineTimeoutSeconds = 3600;
    public const int DefaultJobTimeoutSeconds = 1800;
    public const int DefaultStepTimeoutSeconds = 600;

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Trigger names accepted in YAML, in the casing emitted by <see cref="ToYaml"/>.</summary>
    public static IReadOnlyList<string> TriggerNames { get; } = Enum.GetValues<PipelineTrigger>()
        .Select(TriggerName)
        .ToArray();

    public static string ToYaml(PipelineDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var document = new PipelineYamlDocument
        {
            Name = definition.Name,
            Enabled = definition.Enabled,
            TimeoutSeconds = definition.TimeoutSeconds,
            Triggers = (definition.Triggers ?? []).Select(TriggerName).ToList(),
            Environment = SortedMap(definition.Environment),
            Jobs = (definition.Jobs ?? []).Select(job => new PipelineJobYaml
            {
                Name = job.Name,
                RequiresCapabilities = (job.RequiresCapabilities ?? []).ToList(),
                TimeoutSeconds = job.TimeoutSeconds,
                PublishCheck = job.PublishCheck,
                CheckName = string.IsNullOrWhiteSpace(job.CheckName) ? null : job.CheckName,
                ArtifactGlobs = (job.ArtifactGlobs ?? []).ToList(),
                ContinueOnError = job.ContinueOnError,
                Environment = SortedMap(job.Environment),
                Steps = (job.Steps ?? []).Select(step => new PipelineStepYaml
                {
                    Name = step.Name,
                    Command = step.Command,
                    Shell = string.IsNullOrWhiteSpace(step.Shell) ? null : step.Shell,
                    TimeoutSeconds = step.TimeoutSeconds,
                    ContinueOnError = step.ContinueOnError,
                    Environment = SortedMap(step.Environment)
                }).ToList()
            }).ToList()
        };

        return Serializer.Serialize(document);
    }

    /// <summary>Parses YAML into a definition. Throws <see cref="ArgumentException"/> for anything unusable.</summary>
    public static PipelineDefinition FromYaml(string? yaml)
    {
        var document = Parse(yaml);
        var name = (document.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("Pipeline name is required.");
        }

        if (document.Jobs is null || document.Jobs.Count == 0)
        {
            throw new ArgumentException("A pipeline needs at least one job.");
        }

        var timeoutSeconds = RequirePositive(document.TimeoutSeconds, DefaultPipelineTimeoutSeconds, "Pipeline timeoutSeconds must be greater than zero.");

        return new PipelineDefinition
        {
            Name = name,
            Enabled = document.Enabled ?? true,
            Triggers = MapTriggers(document.Triggers),
            Jobs = document.Jobs.Select(MapJob).ToArray(),
            Environment = MapEnvironment(document.Environment),
            TimeoutSeconds = timeoutSeconds
        };
    }

    /// <summary>Parses YAML into the same request shape the JSON create endpoint accepts.</summary>
    public static CreatePipelineRequest ToCreateRequest(string? yaml)
    {
        var definition = FromYaml(yaml);
        return new CreatePipelineRequest(
            definition.Name,
            definition.Triggers,
            definition.Jobs,
            definition.Environment.ToDictionary(pair => pair.Key, pair => pair.Value),
            definition.TimeoutSeconds);
    }

    /// <summary>Round-trips YAML to canonical form, which doubles as a validation pass.</summary>
    public static string Normalise(string? yaml) => ToYaml(FromYaml(yaml));

    public static bool TryParseTrigger(string? value, out PipelineTrigger trigger)
    {
        trigger = PipelineTrigger.Manual;
        var candidate = Canonicalise(value);
        if (candidate.Length == 0)
        {
            return false;
        }

        foreach (var option in Enum.GetValues<PipelineTrigger>())
        {
            if (Canonicalise(option.ToString()).Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                trigger = option;
                return true;
            }
        }

        return false;
    }

    public static string TriggerName(PipelineTrigger trigger)
    {
        var name = trigger.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static PipelineYamlDocument Parse(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            throw new ArgumentException("Pipeline YAML is empty.");
        }

        PipelineYamlDocument? document;
        try
        {
            document = Deserializer.Deserialize<PipelineYamlDocument>(yaml);
        }
        catch (YamlException exception)
        {
            throw new ArgumentException($"Pipeline YAML could not be parsed: {exception.Message}", exception);
        }

        return document ?? throw new ArgumentException("Pipeline YAML is empty.");
    }

    private static PipelineJobDefinition MapJob(PipelineJobYaml job, int index)
    {
        var name = (job.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException($"Job at position {index + 1} requires a name.");
        }

        if (job.Steps is null || job.Steps.Count == 0)
        {
            throw new ArgumentException($"Job '{name}' needs at least one step.");
        }

        var timeoutSeconds = RequirePositive(job.TimeoutSeconds, DefaultJobTimeoutSeconds, $"Job '{name}' timeoutSeconds must be greater than zero.");
        var steps = job.Steps.Select((step, stepIndex) => MapStep(step, stepIndex, name)).ToArray();

        return new PipelineJobDefinition(
            name,
            steps,
            (job.RequiresCapabilities ?? [])
                .Where(capability => !string.IsNullOrWhiteSpace(capability))
                .Select(capability => capability.Trim())
                .ToArray(),
            MapEnvironment(job.Environment),
            timeoutSeconds,
            job.PublishCheck ?? false,
            string.IsNullOrWhiteSpace(job.CheckName) ? null : job.CheckName.Trim(),
            (job.ArtifactGlobs ?? [])
                .Where(glob => !string.IsNullOrWhiteSpace(glob))
                .Select(glob => glob.Trim())
                .ToArray(),
            job.ContinueOnError ?? false);
    }

    private static PipelineStepDefinition MapStep(PipelineStepYaml step, int index, string jobName)
    {
        var command = (step.Command ?? string.Empty).Trim();
        if (command.Length == 0)
        {
            throw new ArgumentException($"Step at position {index + 1} in job '{jobName}' requires a command.");
        }

        var name = (step.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            name = command;
        }

        var timeoutSeconds = RequirePositive(
            step.TimeoutSeconds,
            DefaultStepTimeoutSeconds,
            $"Step '{name}' in job '{jobName}' timeoutSeconds must be greater than zero.");

        return new PipelineStepDefinition(
            name,
            command,
            string.IsNullOrWhiteSpace(step.Shell) ? null : step.Shell.Trim(),
            MapEnvironment(step.Environment),
            timeoutSeconds,
            step.ContinueOnError ?? false);
    }

    private static IReadOnlyList<PipelineTrigger> MapTriggers(IReadOnlyList<string>? triggers)
    {
        if (triggers is null || triggers.Count == 0)
        {
            return [PipelineTrigger.Manual];
        }

        var mapped = new List<PipelineTrigger>(triggers.Count);
        foreach (var value in triggers)
        {
            if (!TryParseTrigger(value, out var trigger))
            {
                throw new ArgumentException($"Unknown trigger '{value}'. Valid triggers: {string.Join(", ", TriggerNames)}.");
            }

            if (!mapped.Contains(trigger))
            {
                mapped.Add(trigger);
            }
        }

        return mapped;
    }

    private static IReadOnlyDictionary<string, string> MapEnvironment(IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is null || environment.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var mapped = new Dictionary<string, string>(environment.Count, StringComparer.Ordinal);
        foreach (var pair in environment)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new ArgumentException("Environment variable names cannot be blank.");
            }

            mapped[pair.Key.Trim()] = pair.Value ?? string.Empty;
        }

        return mapped;
    }

    private static int RequirePositive(int? value, int fallback, string message)
    {
        if (value is null)
        {
            return fallback;
        }

        if (value <= 0)
        {
            throw new ArgumentException(message);
        }

        return value.Value;
    }

    private static Dictionary<string, string> SortedMap(IReadOnlyDictionary<string, string>? source) =>
        (source ?? new Dictionary<string, string>())
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static string Canonicalise(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
}

public sealed class PipelineYamlDocument
{
    public string? Name { get; set; }
    public bool? Enabled { get; set; }
    public int? TimeoutSeconds { get; set; }
    public List<string>? Triggers { get; set; }
    public Dictionary<string, string>? Environment { get; set; }
    public List<PipelineJobYaml>? Jobs { get; set; }
}

public sealed class PipelineJobYaml
{
    public string? Name { get; set; }
    public List<string>? RequiresCapabilities { get; set; }
    public int? TimeoutSeconds { get; set; }
    public bool? PublishCheck { get; set; }
    public string? CheckName { get; set; }
    public List<string>? ArtifactGlobs { get; set; }
    public bool? ContinueOnError { get; set; }
    public Dictionary<string, string>? Environment { get; set; }
    public List<PipelineStepYaml>? Steps { get; set; }
}

public sealed class PipelineStepYaml
{
    public string? Name { get; set; }
    public string? Command { get; set; }
    public string? Shell { get; set; }
    public int? TimeoutSeconds { get; set; }
    public bool? ContinueOnError { get; set; }
    public Dictionary<string, string>? Environment { get; set; }
}
