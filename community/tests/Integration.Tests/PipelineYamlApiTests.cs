using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

/// <summary>
/// One Core-only install with the Build module added, a single project, and two clients: the owner (holds
/// pipelines.manage) and a member narrowed to the built-in Reader role on that project (does not).
/// </summary>
public sealed class PipelineYamlHostFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;

    public HttpClient Owner { get; private set; } = null!;

    public HttpClient Reader { get; private set; } = null!;

    public Guid ProjectId { get; private set; }

    public async Task InitializeAsync()
    {
        var database = Path.Combine(Path.GetTempPath(), $"forgedeck-pipeyaml-{Guid.NewGuid():N}.db");
        var keys = Path.Combine(Path.GetTempPath(), $"forgedeck-pipeyaml-keys-{Guid.NewGuid():N}");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", $"Data Source={database}");
            builder.UseSetting("Data:ProtectionKeysPath", keys);
            builder.UseSetting("Core:SeedDemoOnEmpty", "false");
            builder.UseSetting("Pipelines:ExecutionMode", "Simulated");
            builder.UseSetting("Bootstrap:Username", "admin");
            builder.UseSetting("Bootstrap:Password", "admin");
        });

        Owner = _factory.CreateClient();
        var setup = await Owner.PostAsJsonAsync("/api/setup", new
        {
            organisationName = "Yaml Labs",
            organisationDescription = "yaml",
            displayName = "Yaml Owner",
            username = "yamlowner",
            email = "owner@yaml.dev",
            password = "password123"
        });
        setup.EnsureSuccessStatusCode();
        var setupBody = await setup.Content.ReadFromJsonAsync<JsonElement>();
        Owner.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", setupBody.GetProperty("token").GetString());

        (await Owner.PostAsJsonAsync("/api/platform/extensions/forgedeck.build/install", new { enable = true }))
            .EnsureSuccessStatusCode();

        var project = await Owner.PostAsJsonAsync("/api/projects", new
        {
            name = "Platform",
            slug = "platform",
            description = "Yaml pipeline host",
            visibility = "Private",
            repositoryMode = "SingleRepository"
        });
        project.EnsureSuccessStatusCode();
        ProjectId = (await project.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Reader = await CreateReaderClientAsync();
    }

    public async Task DisposeAsync()
    {
        Owner.Dispose();
        Reader.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private async Task<HttpClient> CreateReaderClientAsync()
    {
        var member = await Owner.PostAsJsonAsync("/api/organisation/members", new
        {
            email = "reader@yaml.dev",
            username = "yamlreader",
            displayName = "Yaml Reader",
            password = "password123",
            role = "Member"
        });
        member.EnsureSuccessStatusCode();
        var memberId = (await member.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var roles = await Owner.GetFromJsonAsync<JsonElement>("/api/access/roles");
        var readerRoleId = roles.EnumerateArray()
            .First(role => role.GetProperty("slug").GetString() == "reader")
            .GetProperty("id")
            .GetGuid();

        var team = await Owner.PostAsJsonAsync("/api/teams", new
        {
            name = "Yaml readers",
            slug = "yaml-readers",
            description = "Read-only",
            roleId = readerRoleId
        });
        team.EnsureSuccessStatusCode();
        var teamId = (await team.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await Owner.PostAsJsonAsync($"/api/teams/{teamId}/members", new { userId = memberId })).EnsureSuccessStatusCode();
        (await Owner.PostAsJsonAsync($"/api/projects/{ProjectId}/teams", new { teamId })).EnsureSuccessStatusCode();

        var client = _factory!.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "reader@yaml.dev", password = "password123" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

public sealed class PipelineYamlApiTests(PipelineYamlHostFixture host) : IClassFixture<PipelineYamlHostFixture>
{
    private const string ValidYaml = """
        name: Api Sample
        enabled: true
        timeoutSeconds: 2400
        triggers:
          - manual
          - push
        environment:
          DOTNET_NOLOGO: 'true'
        jobs:
          - name: Build
            requiresCapabilities:
              - dotnet
            timeoutSeconds: 1200
            publishCheck: true
            checkName: Build
            artifactGlobs:
              - 'artifacts/**'
            steps:
              - name: Restore
                command: dotnet restore
                timeoutSeconds: 300
              - name: Compile
                command: dotnet build --no-restore
                timeoutSeconds: 600
        """;

    public static TheoryData<string> CatalogueTemplateIds() =>
        new("dotnet-build", "dotnet-test", "node-build", "docker-build", "shell-script", "publish-artifacts");

    public static TheoryData<string> InvalidYamlDocuments() =>
        new(
            "",
            "   ",
            "\n\n",
            "name: Missing Jobs",
            "name: Missing Jobs\njobs: []",
            "jobs:\n  - name: Build\n    steps:\n      - command: echo hi",
            "name: ''\njobs:\n  - name: Build\n    steps:\n      - command: echo hi",
            "name: P\njobs:\n  - steps:\n      - command: echo hi",
            "name: P\njobs:\n  - name: Build",
            "name: P\njobs:\n  - name: Build\n    steps: []",
            "name: P\njobs:\n  - name: Build\n    steps:\n      - name: NoCommand",
            "name: P\ntimeoutSeconds: 0\njobs:\n  - name: Build\n    steps:\n      - command: echo hi",
            "name: P\ntimeoutSeconds: -30\njobs:\n  - name: Build\n    steps:\n      - command: echo hi",
            "name: P\njobs:\n  - name: Build\n    timeoutSeconds: 0\n    steps:\n      - command: echo hi",
            "name: P\njobs:\n  - name: Build\n    steps:\n      - command: echo hi\n        timeoutSeconds: -1",
            "name: P\ntriggers: [nightly]\njobs:\n  - name: Build\n    steps:\n      - command: echo hi",
            "name: P\ntriggers: [cron]\njobs:\n  - name: Build\n    steps:\n      - command: echo hi",
            "name: [unclosed",
            "- just\n- a\n- list",
            "name: ok\njobs: 42",
            "name: ok\ntimeoutSeconds: not-a-number");

    public static TheoryData<string, string> TriggerAliases() =>
        new()
        {
            { "manual", "Manual" },
            { "MANUAL", "Manual" },
            { "Manual", "Manual" },
            { "push", "Push" },
            { "PUSH", "Push" },
            { "changeOpened", "ChangeOpened" },
            { "changeopened", "ChangeOpened" },
            { "CHANGEOPENED", "ChangeOpened" },
            { "change-opened", "ChangeOpened" },
            { "change_opened", "ChangeOpened" },
            { "changeUpdated", "ChangeUpdated" },
            { "CHANGE_UPDATED", "ChangeUpdated" },
            { "changeMerged", "ChangeMerged" },
            { "change-merged", "ChangeMerged" }
        };

    [Fact]
    public async Task Job_templates_are_served()
    {
        var templates = await host.Owner.GetFromJsonAsync<JsonElement>("/api/pipelines/job-templates");

        Assert.Equal(JsonValueKind.Array, templates.ValueKind);
        Assert.NotEmpty(templates.EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(CatalogueTemplateIds))]
    public async Task Job_template_is_present_and_well_formed(string id)
    {
        var templates = await host.Owner.GetFromJsonAsync<JsonElement>("/api/pipelines/job-templates");
        var template = templates.EnumerateArray().Single(entry => entry.GetProperty("id").GetString() == id);

        Assert.False(string.IsNullOrWhiteSpace(template.GetProperty("name").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(template.GetProperty("description").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(template.GetProperty("category").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(template.GetProperty("icon").GetString()));

        var job = template.GetProperty("job");
        Assert.False(string.IsNullOrWhiteSpace(job.GetProperty("name").GetString()));
        Assert.True(job.GetProperty("timeoutSeconds").GetInt32() > 0);
        Assert.NotEmpty(job.GetProperty("steps").EnumerateArray());
        Assert.All(job.GetProperty("steps").EnumerateArray(), step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.GetProperty("command").GetString()));
            Assert.True(step.GetProperty("timeoutSeconds").GetInt32() > 0);
        });
    }

    [Fact]
    public async Task Job_templates_are_readable_without_manage_permission()
    {
        var response = await host.Reader.GetAsync("/api/pipelines/job-templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(CatalogueTemplateIds))]
    public async Task Every_catalogue_job_can_be_created_through_from_yaml(string id)
    {
        var templates = await host.Owner.GetFromJsonAsync<JsonElement>("/api/pipelines/job-templates");
        var job = templates.EnumerateArray().Single(entry => entry.GetProperty("id").GetString() == id).GetProperty("job");
        var yaml = YamlForTemplate($"Catalogue {id} {Guid.NewGuid():N}", job);

        var created = await host.Owner.PostAsJsonAsync("/api/pipelines/definitions/from-yaml", new { yaml });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var definition = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(job.GetProperty("name").GetString(), definition.GetProperty("jobs")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Validate_yaml_accepts_a_complete_document()
    {
        var response = await host.Owner.PostAsJsonAsync("/api/pipelines/definitions/validate-yaml", new { yaml = ValidYaml });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("valid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("error").ValueKind);
        Assert.Equal("Api Sample", body.GetProperty("definition").GetProperty("name").GetString());
        Assert.Equal(2400, body.GetProperty("definition").GetProperty("timeoutSeconds").GetInt32());
        Assert.Contains("timeoutSeconds:", body.GetProperty("yaml").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_yaml_is_open_to_readers()
    {
        var response = await host.Reader.PostAsJsonAsync("/api/pipelines/definitions/validate-yaml", new { yaml = ValidYaml });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InvalidYamlDocuments))]
    public async Task Validate_yaml_rejects_broken_documents(string yaml)
    {
        var response = await host.Owner.PostAsJsonAsync("/api/pipelines/definitions/validate-yaml", new { yaml });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("valid").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    [Theory]
    [MemberData(nameof(InvalidYamlDocuments))]
    public async Task From_yaml_rejects_broken_documents(string yaml)
    {
        var response = await host.Owner.PostAsJsonAsync("/api/pipelines/definitions/from-yaml", new { yaml });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()));
    }

    [Theory]
    [MemberData(nameof(TriggerAliases))]
    public async Task Validate_yaml_normalises_trigger_aliases(string alias, string expected)
    {
        var yaml = $"name: Trigger {alias}\ntriggers: [{alias}]\njobs:\n  - name: Build\n    steps:\n      - command: echo hi";

        var response = await host.Owner.PostAsJsonAsync("/api/pipelines/definitions/validate-yaml", new { yaml });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var triggers = body.GetProperty("definition").GetProperty("triggers").EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.Equal([expected], triggers);
    }

    [Fact]
    public async Task Validate_yaml_defaults_triggers_to_manual()
    {
        var yaml = "name: Defaults\njobs:\n  - name: Build\n    steps:\n      - command: echo hi";

        var body = await PostJsonAsync("/api/pipelines/definitions/validate-yaml", new { yaml });
        var definition = body.GetProperty("definition");

        Assert.Equal(["Manual"], definition.GetProperty("triggers").EnumerateArray().Select(t => t.GetString()).ToArray());
        Assert.Equal(3600, definition.GetProperty("timeoutSeconds").GetInt32());
        Assert.Equal(1800, definition.GetProperty("jobs")[0].GetProperty("timeoutSeconds").GetInt32());
        Assert.Equal(600, definition.GetProperty("jobs")[0].GetProperty("steps")[0].GetProperty("timeoutSeconds").GetInt32());
    }

    [Fact]
    public async Task Validate_yaml_defaults_a_step_name_to_its_command()
    {
        var yaml = "name: Step Names\njobs:\n  - name: Build\n    steps:\n      - command: dotnet build";

        var body = await PostJsonAsync("/api/pipelines/definitions/validate-yaml", new { yaml });

        Assert.Equal(
            "dotnet build",
            body.GetProperty("definition").GetProperty("jobs")[0].GetProperty("steps")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Validate_yaml_output_is_itself_valid()
    {
        var first = await PostJsonAsync("/api/pipelines/definitions/validate-yaml", new { yaml = ValidYaml });
        var normalised = first.GetProperty("yaml").GetString();

        var second = await PostJsonAsync("/api/pipelines/definitions/validate-yaml", new { yaml = normalised });

        Assert.True(second.GetProperty("valid").GetBoolean());
        Assert.Equal(normalised, second.GetProperty("yaml").GetString());
    }

    [Fact]
    public async Task From_yaml_creates_a_definition_that_reads_back()
    {
        var name = $"Created {Guid.NewGuid():N}";
        var created = await host.Owner.PostAsJsonAsync(
            "/api/pipelines/definitions/from-yaml",
            new { yaml = ValidYaml.Replace("name: Api Sample", $"name: {name}", StringComparison.Ordinal) });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var definition = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = definition.GetProperty("id").GetGuid();
        Assert.Equal($"/api/pipelines/definitions/{id}", created.Headers.Location?.ToString());

        var fetched = await host.Owner.GetFromJsonAsync<JsonElement>($"/api/pipelines/definitions/{id}");
        Assert.Equal(name, fetched.GetProperty("name").GetString());
        Assert.True(fetched.GetProperty("enabled").GetBoolean());
        Assert.Equal(2400, fetched.GetProperty("timeoutSeconds").GetInt32());
        Assert.Equal(
            ["Manual", "Push"],
            fetched.GetProperty("triggers").EnumerateArray().Select(t => t.GetString()).ToArray());
        Assert.Equal("Build", fetched.GetProperty("jobs")[0].GetProperty("name").GetString());
        Assert.Equal(2, fetched.GetProperty("jobs")[0].GetProperty("steps").GetArrayLength());
        Assert.Equal("true", fetched.GetProperty("environment").GetProperty("DOTNET_NOLOGO").GetString());
    }

    [Fact]
    public async Task From_yaml_honours_a_disabled_pipeline()
    {
        var yaml = $"name: Disabled {Guid.NewGuid():N}\nenabled: false\njobs:\n  - name: Build\n    steps:\n      - command: echo hi";

        var created = await host.Owner.PostAsJsonAsync("/api/pipelines/definitions/from-yaml", new { yaml });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var fetched = await host.Owner.GetFromJsonAsync<JsonElement>($"/api/pipelines/definitions/{id}");
        Assert.False(fetched.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task From_yaml_appears_in_the_definition_list()
    {
        var name = $"Listed {Guid.NewGuid():N}";
        var created = await host.Owner.PostAsJsonAsync(
            "/api/pipelines/definitions/from-yaml",
            new { yaml = $"name: {name}\njobs:\n  - name: Build\n    steps:\n      - command: echo hi" });
        created.EnsureSuccessStatusCode();

        var definitions = await host.Owner.GetFromJsonAsync<JsonElement>("/api/pipelines/definitions");

        Assert.Contains(definitions.EnumerateArray(), entry => entry.GetProperty("name").GetString() == name);
    }

    [Fact]
    public async Task From_yaml_requires_manage_permission()
    {
        var response = await host.Reader.PostAsJsonAsync("/api/pipelines/definitions/from-yaml", new { yaml = ValidYaml });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Definition_yaml_round_trips_through_the_api()
    {
        var id = await CreateAsync($"Round trip {Guid.NewGuid():N}");

        var yamlResponse = await host.Owner.GetFromJsonAsync<JsonElement>($"/api/pipelines/definitions/{id}/yaml");
        var yaml = yamlResponse.GetProperty("yaml").GetString()!;

        Assert.Equal(id, yamlResponse.GetProperty("id").GetGuid());
        Assert.Equal(1, yamlResponse.GetProperty("version").GetInt32());
        Assert.Contains("jobs:", yaml, StringComparison.Ordinal);
        Assert.Contains("steps:", yaml, StringComparison.Ordinal);

        var validated = await PostJsonAsync("/api/pipelines/definitions/validate-yaml", new { yaml });
        Assert.True(validated.GetProperty("valid").GetBoolean());
        Assert.Equal(
            yamlResponse.GetProperty("name").GetString(),
            validated.GetProperty("definition").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Definition_yaml_is_readable_without_manage_permission()
    {
        var id = await CreateAsync($"Reader read {Guid.NewGuid():N}");

        var response = await host.Reader.GetAsync($"/api/pipelines/definitions/{id}/yaml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Definition_yaml_is_not_found_for_an_unknown_id()
    {
        var response = await host.Owner.GetAsync($"/api/pipelines/definitions/{Guid.NewGuid()}/yaml");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_yaml_updates_name_jobs_and_version()
    {
        var id = await CreateAsync($"Before {Guid.NewGuid():N}");
        var updatedName = $"After {Guid.NewGuid():N}";
        var yaml = $"""
            name: {updatedName}
            timeoutSeconds: 1800
            triggers:
              - push
              - changeOpened
            jobs:
              - name: Compile
                timeoutSeconds: 900
                steps:
                  - name: Build
                    command: dotnet build
                    timeoutSeconds: 450
              - name: Verify
                timeoutSeconds: 900
                steps:
                  - name: Test
                    command: dotnet test
                    timeoutSeconds: 450
            """;

        var updated = await host.Owner.PutAsJsonAsync($"/api/pipelines/definitions/{id}/yaml", new { yaml });
        updated.EnsureSuccessStatusCode();

        var fetched = await host.Owner.GetFromJsonAsync<JsonElement>($"/api/pipelines/definitions/{id}");
        Assert.Equal(updatedName, fetched.GetProperty("name").GetString());
        Assert.Equal(1800, fetched.GetProperty("timeoutSeconds").GetInt32());
        Assert.Equal(2, fetched.GetProperty("jobs").GetArrayLength());
        Assert.Equal("Compile", fetched.GetProperty("jobs")[0].GetProperty("name").GetString());
        Assert.Equal("Verify", fetched.GetProperty("jobs")[1].GetProperty("name").GetString());
        Assert.Equal(
            ["Push", "ChangeOpened"],
            fetched.GetProperty("triggers").EnumerateArray().Select(t => t.GetString()).ToArray());
        Assert.True(fetched.GetProperty("version").GetInt32() > 1);
    }

    [Fact]
    public async Task Put_yaml_rejects_a_broken_document()
    {
        var id = await CreateAsync($"Broken put {Guid.NewGuid():N}");

        var response = await host.Owner.PutAsJsonAsync($"/api/pipelines/definitions/{id}/yaml", new { yaml = "name: Only" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_yaml_requires_manage_permission()
    {
        var id = await CreateAsync($"Reader put {Guid.NewGuid():N}");

        var response = await host.Reader.PutAsJsonAsync($"/api/pipelines/definitions/{id}/yaml", new { yaml = ValidYaml });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_yaml_is_not_found_for_an_unknown_id()
    {
        var response = await host.Owner.PutAsJsonAsync(
            $"/api/pipelines/definitions/{Guid.NewGuid()}/yaml",
            new { yaml = $"name: Ghost {Guid.NewGuid():N}\njobs:\n  - name: Build\n    steps:\n      - command: echo hi" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Json_create_still_works_alongside_the_yaml_endpoints()
    {
        var name = $"Json created {Guid.NewGuid():N}";
        var created = await host.Owner.PostAsJsonAsync("/api/pipelines/definitions", new
        {
            name,
            triggers = new[] { "Manual", "Push" },
            timeoutSeconds = 3000,
            environment = new Dictionary<string, string> { ["CI"] = "true" },
            jobs = new[]
            {
                new
                {
                    name = "Build",
                    steps = new[]
                    {
                        new
                        {
                            name = "Compile",
                            command = "dotnet build",
                            shell = (string?)null,
                            environment = new Dictionary<string, string>(),
                            timeoutSeconds = 600,
                            continueOnError = false
                        }
                    },
                    requiresCapabilities = new[] { "dotnet" },
                    environment = new Dictionary<string, string>(),
                    timeoutSeconds = 1200,
                    publishCheck = true,
                    checkName = "Build",
                    artifactGlobs = Array.Empty<string>(),
                    continueOnError = false
                }
            }
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var yaml = (await host.Owner.GetFromJsonAsync<JsonElement>($"/api/pipelines/definitions/{id}/yaml"))
            .GetProperty("yaml").GetString()!;
        var validated = await PostJsonAsync("/api/pipelines/definitions/validate-yaml", new { yaml });

        Assert.True(validated.GetProperty("valid").GetBoolean());
        Assert.Equal(name, validated.GetProperty("definition").GetProperty("name").GetString());
        Assert.Equal(3000, validated.GetProperty("definition").GetProperty("timeoutSeconds").GetInt32());
    }

    [Fact]
    public async Task Json_create_requires_manage_permission()
    {
        var response = await host.Reader.PostAsJsonAsync("/api/pipelines/definitions", new
        {
            name = "Reader json",
            triggers = new[] { "Manual" },
            jobs = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Yaml_created_pipeline_can_be_disabled_and_enabled()
    {
        var id = await CreateAsync($"Toggle {Guid.NewGuid():N}");

        (await host.Owner.PostAsync($"/api/pipelines/definitions/{id}/disable", null)).EnsureSuccessStatusCode();
        Assert.False((await host.Owner.GetFromJsonAsync<JsonElement>($"/api/pipelines/definitions/{id}")).GetProperty("enabled").GetBoolean());

        (await host.Owner.PostAsync($"/api/pipelines/definitions/{id}/enable", null)).EnsureSuccessStatusCode();
        Assert.True((await host.Owner.GetFromJsonAsync<JsonElement>($"/api/pipelines/definitions/{id}")).GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Yaml_preserves_environment_maps_at_every_level()
    {
        var name = $"Env {Guid.NewGuid():N}";
        var yaml = $"""
            name: {name}
            environment:
              PIPE: one
            jobs:
              - name: Build
                environment:
                  JOB: two
                steps:
                  - command: echo hi
                    environment:
                      STEP: three
            """;

        var created = await host.Owner.PostAsJsonAsync("/api/pipelines/definitions/from-yaml", new { yaml });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var roundTripped = (await host.Owner.GetFromJsonAsync<JsonElement>($"/api/pipelines/definitions/{id}/yaml"))
            .GetProperty("yaml").GetString()!;
        var definition = (await PostJsonAsync("/api/pipelines/definitions/validate-yaml", new { yaml = roundTripped }))
            .GetProperty("definition");

        Assert.Equal("one", definition.GetProperty("environment").GetProperty("PIPE").GetString());
        Assert.Equal("two", definition.GetProperty("jobs")[0].GetProperty("environment").GetProperty("JOB").GetString());
        Assert.Equal("three", definition.GetProperty("jobs")[0].GetProperty("steps")[0].GetProperty("environment").GetProperty("STEP").GetString());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(3600)]
    [InlineData(86400)]
    public async Task Explicit_timeouts_survive_the_yaml_round_trip(int seconds)
    {
        var yaml = $"name: Timeout {seconds} {Guid.NewGuid():N}\ntimeoutSeconds: {seconds}\njobs:\n  - name: Build\n    steps:\n      - command: echo hi";

        var created = await host.Owner.PostAsJsonAsync("/api/pipelines/definitions/from-yaml", new { yaml });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var fetched = await host.Owner.GetFromJsonAsync<JsonElement>($"/api/pipelines/definitions/{id}");
        Assert.Equal(seconds, fetched.GetProperty("timeoutSeconds").GetInt32());
    }

    [Fact]
    public async Task Unknown_yaml_properties_are_ignored()
    {
        var yaml = $"""
            name: Loose {Guid.NewGuid():N}
            futureFlag: true
            jobs:
              - name: Build
                somethingElse: 12
                steps:
                  - command: echo hi
                    extra: true
            """;

        var body = await PostJsonAsync("/api/pipelines/definitions/validate-yaml", new { yaml });

        Assert.True(body.GetProperty("valid").GetBoolean());
        Assert.Equal(1, body.GetProperty("definition").GetProperty("jobs").GetArrayLength());
    }

    [Fact]
    public async Task Duplicate_triggers_collapse()
    {
        var yaml = "name: Duplicates\ntriggers: [manual, Manual, push, PUSH]\njobs:\n  - name: Build\n    steps:\n      - command: echo hi";

        var body = await PostJsonAsync("/api/pipelines/definitions/validate-yaml", new { yaml });

        Assert.Equal(
            ["Manual", "Push"],
            body.GetProperty("definition").GetProperty("triggers").EnumerateArray().Select(t => t.GetString()).ToArray());
    }

    [Fact]
    public async Task Deleting_a_yaml_created_pipeline_removes_its_yaml()
    {
        var id = await CreateAsync($"Deleted {Guid.NewGuid():N}");

        (await host.Owner.DeleteAsync($"/api/pipelines/definitions/{id}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await host.Owner.GetAsync($"/api/pipelines/definitions/{id}/yaml")).StatusCode);
    }

    private async Task<Guid> CreateAsync(string name)
    {
        var created = await host.Owner.PostAsJsonAsync(
            "/api/pipelines/definitions/from-yaml",
            new { yaml = ValidYaml.Replace("name: Api Sample", $"name: {name}", StringComparison.Ordinal) });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> PostJsonAsync(string path, object body)
    {
        var response = await host.Owner.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string YamlForTemplate(string name, JsonElement job)
    {
        var lines = new List<string>
        {
            $"name: {name}",
            "jobs:",
            $"  - name: {Quote(job.GetProperty("name").GetString())}",
            $"    timeoutSeconds: {job.GetProperty("timeoutSeconds").GetInt32()}",
            $"    publishCheck: {(job.GetProperty("publishCheck").GetBoolean() ? "true" : "false")}"
        };

        if (job.TryGetProperty("checkName", out var checkName) && checkName.ValueKind == JsonValueKind.String)
        {
            lines.Add($"    checkName: {Quote(checkName.GetString())}");
        }

        var capabilities = job.GetProperty("requiresCapabilities").EnumerateArray().Select(c => c.GetString()).ToArray();
        if (capabilities.Length > 0)
        {
            lines.Add("    requiresCapabilities:");
            lines.AddRange(capabilities.Select(c => $"      - {Quote(c)}"));
        }

        var globs = job.GetProperty("artifactGlobs").EnumerateArray().Select(g => g.GetString()).ToArray();
        if (globs.Length > 0)
        {
            lines.Add("    artifactGlobs:");
            lines.AddRange(globs.Select(g => $"      - {Quote(g)}"));
        }

        lines.Add("    steps:");
        foreach (var step in job.GetProperty("steps").EnumerateArray())
        {
            lines.Add($"      - name: {Quote(step.GetProperty("name").GetString())}");
            lines.Add($"        command: {Quote(step.GetProperty("command").GetString())}");
            lines.Add($"        timeoutSeconds: {step.GetProperty("timeoutSeconds").GetInt32()}");
            if (step.TryGetProperty("shell", out var shell) && shell.ValueKind == JsonValueKind.String)
            {
                lines.Add($"        shell: {Quote(shell.GetString())}");
            }
        }

        return string.Join('\n', lines);
    }

    private static string Quote(string? value) => $"'{(value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal)}'";
}
