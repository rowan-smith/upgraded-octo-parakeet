using Microsoft.Playwright;
using Platform.Contracts.Capabilities;
using System.Text.Json;

namespace Tests.E2E.Playwright;

[Collection("playwright")]
public sealed class LicensingPlaywrightTests(PlaywrightBrowserFixture browser)
{
    [Fact]
    public async Task Community_setup_shows_community_editions_and_hides_commercial_capabilities()
    {
        await using var host = ForgeDeckHost.StartEmpty();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupAsync(page, org: "Community Labs", email: "community@example.com", username: "community");

        await Ui.OpenLicensingAsync(page);
        await Assertions.Expect(page.Locator("#licensingMode")).ToHaveTextAsync("Community");
        await Assertions.Expect(page.Locator("#licensingStatusPill")).ToHaveTextAsync("Community");
        await Assertions.Expect(page.Locator("tr[data-licence-module='review']")).ToContainTextAsync("Community");
        await Assertions.Expect(page.Locator("tr[data-licence-module='pipelines']")).ToContainTextAsync("Community");

        await Ui.OpenModulesAsync(page);
        await Assertions.Expect(page.Locator("[data-module-id='review']")).ToHaveAttributeAsync("data-edition", "Community");
        await Assertions.Expect(page.Locator("[data-module-id='review'] [data-capabilities]"))
            .ToContainTextAsync(KnownCapabilities.Review.BasicApproval);
        await Assertions.Expect(page.Locator("[data-module-id='review'] [data-capabilities]"))
            .Not.ToContainTextAsync(KnownCapabilities.Review.MultiApproval);
        await Assertions.Expect(page.Locator("[data-module-id='pipelines']")).ToHaveAttributeAsync("data-edition", "Community");
        // EE package may be present; commercial capabilities stay ungated until licenced.
        if (await page.Locator("[data-module-id='review-commercial']").CountAsync() > 0)
        {
            await Assertions.Expect(page.Locator("[data-module-id='review-commercial'] [data-capabilities]"))
                .Not.ToContainTextAsync(KnownCapabilities.Review.MultiApproval);
        }

        var policy = await GetJsonAsync(page, "/api/review/policy");
        Assert.False(policy.GetProperty("multiApprovalLicensed").GetBoolean());
        Assert.Equal(1, policy.GetProperty("minimumApprovals").GetInt32());
    }

    [Fact]
    public async Task Commercial_licence_during_setup_unlocks_review_commercial_visibility()
    {
        using var licences = new TestLicenceFactory();
        await using var host = ForgeDeckHost.StartEmptyWithLicenceKey(licences.PublicKeyPem);
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        var payload = licences.CreateCommercialJson();
        await Ui.CompleteSetupWithCommercialLicenceAsync(page, payload, org: "Northstar Commercial", email: "comm@example.com", username: "commercial");

        await Ui.OpenLicensingAsync(page);
        await Assertions.Expect(page.Locator("#licensingMode")).ToHaveTextAsync("Commercial");
        await Assertions.Expect(page.Locator("#licensingStatusPill")).ToHaveTextAsync("Active");
        await Assertions.Expect(page.Locator("tr[data-licence-module='review']")).ToContainTextAsync("Commercial");
        await Assertions.Expect(page.Locator("tr[data-licence-module='pipelines']")).ToContainTextAsync("Community");

        await Ui.OpenModulesAsync(page);
        await Assertions.Expect(page.Locator("[data-module-id='review']")).ToHaveAttributeAsync("data-edition", "Commercial");
        await Assertions.Expect(page.Locator("[data-module-id='review'] [data-capabilities]"))
            .ToContainTextAsync(KnownCapabilities.Review.BasicApproval);
        await Assertions.Expect(page.Locator("[data-module-id='review'] [data-capabilities]"))
            .Not.ToContainTextAsync(KnownCapabilities.Review.MultiApproval);
        await Assertions.Expect(page.Locator("[data-module-id='review-commercial']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-module-id='review-commercial']")).ToHaveAttributeAsync("data-edition", "Commercial");
        await Assertions.Expect(page.Locator("[data-module-id='review-commercial'] [data-capabilities]"))
            .ToContainTextAsync(KnownCapabilities.Review.MultiApproval);

        var policy = await GetJsonAsync(page, "/api/review/policy");
        Assert.True(policy.GetProperty("multiApprovalLicensed").GetBoolean());
    }

    [Fact]
    public async Task Invalid_licence_during_setup_is_rejected_and_community_remains_available()
    {
        using var licences = new TestLicenceFactory();
        await using var host = ForgeDeckHost.StartEmptyWithLicenceKey(licences.PublicKeyPem);
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupThroughLicenceAsync(page, "Invalid Licence Org");
        await page.Locator("#setupLicencePayload").FillAsync(licences.CreateTamperedJson());
        await page.Locator("#setupValidateLicence").ClickAsync();
        await Ui.ExpectToastAsync(page, "Licence could not be validated");
        await Ui.ExpectVisible(page, "h1", "Choose your licence");

        await page.Locator("#setupUseCommunity").ClickAsync();
        await Ui.CompleteSetupAfterLicenceAsync(page, "ok@example.com", "okowner", "Invalid Licence Org");

        await Ui.OpenLicensingAsync(page);
        await Assertions.Expect(page.Locator("#licensingMode")).ToHaveTextAsync("Community");
    }

    [Fact]
    public async Task Invalid_replacement_keeps_active_commercial_licence()
    {
        using var licences = new TestLicenceFactory();
        await using var host = ForgeDeckHost.StartEmptyWithLicenceKey(licences.PublicKeyPem);
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupWithCommercialLicenceAsync(
            page,
            licences.CreateCommercialJson(),
            org: "Replace Org",
            email: "replace@example.com",
            username: "replaceowner");

        await Ui.OpenLicensingAsync(page);
        await Assertions.Expect(page.Locator("#licensingMode")).ToHaveTextAsync("Commercial");

        await page.Locator("#licensingPayload").FillAsync("{ \"not\": \"a licence\" }");
        await page.Locator("#licensingInstall").ClickAsync();
        await Ui.ExpectToastAsync(page, "Licence could not be validated");
        await Assertions.Expect(page.Locator("#licensingMode")).ToHaveTextAsync("Commercial");
        await Assertions.Expect(page.Locator("tr[data-licence-module='review']")).ToContainTextAsync("Commercial");

        var policy = await GetJsonAsync(page, "/api/review/policy");
        Assert.True(policy.GetProperty("multiApprovalLicensed").GetBoolean());
    }

    [Fact]
    public async Task Removing_commercial_licence_returns_modules_to_community()
    {
        using var licences = new TestLicenceFactory();
        await using var host = ForgeDeckHost.StartEmptyWithLicenceKey(licences.PublicKeyPem);
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupWithCommercialLicenceAsync(
            page,
            licences.CreateCommercialJson(),
            org: "Remove Org",
            email: "remove@example.com",
            username: "removeowner");

        await Ui.OpenLicensingAsync(page);
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await page.Locator("#licensingRemove").ClickAsync();
        await Ui.ExpectToastAsync(page, "Commercial licence removed");
        await Assertions.Expect(page.Locator("#licensingMode")).ToHaveTextAsync("Community");
        await Assertions.Expect(page.Locator("tr[data-licence-module='review']")).ToContainTextAsync("Community");

        await Ui.OpenModulesAsync(page);
        await Assertions.Expect(page.Locator("[data-module-id='review']")).ToHaveAttributeAsync("data-edition", "Community");
        await Assertions.Expect(page.Locator("[data-module-id='review'] [data-capabilities]"))
            .Not.ToContainTextAsync(KnownCapabilities.Review.MultiApproval);

        var policy = await GetJsonAsync(page, "/api/review/policy");
        Assert.False(policy.GetProperty("multiApprovalLicensed").GetBoolean());
    }

    [Fact]
    public async Task Community_blocks_multi_approval_policy_api_while_commercial_allows_it()
    {
        using var licences = new TestLicenceFactory();
        await using var host = ForgeDeckHost.StartEmptyWithLicenceKey(licences.PublicKeyPem);
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupAsync(page, org: "Policy Org", email: "policy@example.com", username: "policyowner");

        var denied = await page.EvaluateAsync<JsonElement>("""
            async () => {
              const token = localStorage.getItem('forgedeck.token');
              const response = await fetch('/api/review/policy', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
                body: JSON.stringify({ minimumApprovals: 2 })
              });
              return { status: response.status, body: await response.json() };
            }
            """);
        Assert.Equal(403, denied.GetProperty("status").GetInt32());
        Assert.Equal(KnownCapabilities.Review.MultiApproval, denied.GetProperty("body").GetProperty("capability").GetString());

        await Ui.OpenLicensingAsync(page);
        await page.Locator("#licensingPayload").FillAsync(licences.CreateCommercialJson());
        await page.Locator("#licensingInstall").ClickAsync();
        await Ui.ExpectToastAsync(page, "Licence installed");

        var allowed = await page.EvaluateAsync<JsonElement>("""
            async () => {
              const token = localStorage.getItem('forgedeck.token');
              const response = await fetch('/api/review/policy', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
                body: JSON.stringify({ minimumApprovals: 2 })
              });
              return { status: response.status, body: await response.json() };
            }
            """);
        Assert.Equal(200, allowed.GetProperty("status").GetInt32());
        Assert.True(allowed.GetProperty("body").GetProperty("multiApprovalLicensed").GetBoolean());
        Assert.Equal(2, allowed.GetProperty("body").GetProperty("minimumApprovals").GetInt32());

        await Ui.OpenModulesAsync(page);
        await Assertions.Expect(page.Locator("[data-module-id='review']")).ToHaveAttributeAsync("data-edition", "Commercial");
    }

    [Fact]
    public async Task Platform_modules_api_reflects_licence_grants()
    {
        using var licences = new TestLicenceFactory();
        await using var host = ForgeDeckHost.StartEmptyWithLicenceKey(licences.PublicKeyPem);
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.CompleteSetupAsync(page, org: "Api Org", email: "api@example.com", username: "apiowner");

        var community = await GetJsonAsync(page, "/api/platform/modules");
        Assert.DoesNotContain(KnownCapabilities.Review.MultiApproval,
            community.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString()));
        var reviewCommunity = community.GetProperty("modules").EnumerateArray()
            .First(m => m.GetProperty("id").GetString() == "review");
        Assert.Equal("Community", reviewCommunity.GetProperty("edition").GetString());

        await Ui.OpenLicensingAsync(page);
        await page.Locator("#licensingPayload").FillAsync(licences.CreateCommercialJson(
            reviewCapabilities: [KnownCapabilities.Review.MultiApproval, KnownCapabilities.Review.CodeOwners]));
        await page.Locator("#licensingInstall").ClickAsync();
        await Ui.ExpectToastAsync(page, "Licence installed");

        var commercial = await GetJsonAsync(page, "/api/platform/modules");
        var caps = commercial.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString()).ToHashSet();
        Assert.Contains(KnownCapabilities.Review.MultiApproval, caps);
        Assert.Contains(KnownCapabilities.Review.CodeOwners, caps);
        var reviewCommercial = commercial.GetProperty("modules").EnumerateArray()
            .First(m => m.GetProperty("id").GetString() == "review");
        Assert.Equal("Commercial", reviewCommercial.GetProperty("edition").GetString());
    }

    [Fact]
    public async Task Seeded_installation_defaults_to_community_module_visibility()
    {
        await using var host = ForgeDeckHost.StartSeeded();
        await using var session = await host.NewPageAsync(browser.Browser);
        var page = session.Page;

        await Ui.OpenLicensingAsync(page);
        await Assertions.Expect(page.Locator("#licensingMode")).ToHaveTextAsync("Community");
        await Assertions.Expect(page.Locator("tr[data-licence-module='review']")).ToContainTextAsync("Community");

        await Ui.OpenModulesAsync(page);
        await Assertions.Expect(page.Locator("[data-module-id='review']")).ToHaveAttributeAsync("data-edition", "Community");
        await Assertions.Expect(page.Locator("[data-module-id='review'] [data-capabilities]"))
            .Not.ToContainTextAsync(KnownCapabilities.Review.MultiApproval);
    }

    private static async Task<JsonElement> GetJsonAsync(IPage page, string path)
    {
        return await page.EvaluateAsync<JsonElement>($$"""
            async () => {
              const token = localStorage.getItem('forgedeck.token');
              const response = await fetch('{{path}}', {
                headers: token ? { Authorization: `Bearer ${token}` } : {}
              });
              if (!response.ok) throw new Error('Request failed ' + response.status);
              return await response.json();
            }
            """);
    }
}
