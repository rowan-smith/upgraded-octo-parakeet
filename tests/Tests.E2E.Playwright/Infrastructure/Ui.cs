using Microsoft.Playwright;

namespace Tests.E2E.Playwright;

internal static class Ui
{
    public static async Task CompleteSetupAsync(IPage page, string org = "Northstar Engineering", string email = "rowan@example.com", string username = "rowan")
    {
        await CompleteSetupThroughLicenceAsync(page, org);
        await page.Locator("#setupUseCommunity").ClickAsync();
        await CompleteSetupAfterLicenceAsync(page, email, username, org);
    }

    public static async Task CompleteSetupWithCommercialLicenceAsync(
        IPage page,
        string licenceJson,
        string org = "Northstar Engineering",
        string email = "rowan@example.com",
        string username = "rowan")
    {
        await CompleteSetupThroughLicenceAsync(page, org);
        await page.Locator("#setupLicencePayload").FillAsync(licenceJson);
        await page.Locator("#setupValidateLicence").ClickAsync();
        await ExpectVisible(page, "h1", "Create Owner Account");
        await CompleteSetupAfterLicenceAsync(page, email, username, org);
    }

    public static async Task CompleteSetupThroughLicenceAsync(IPage page, string org = "Northstar Engineering")
    {
        await ExpectVisible(page, "h1", "Initial Setup");
        await page.Locator("#bootstrapSignIn").ClickAsync();
        await ExpectVisible(page, "h1", "Set up your organisation");
        await page.Locator("#setupOrgName").FillAsync(org);
        await page.Locator("#setupOrgContinue").ClickAsync();
        await ExpectVisible(page, "h1", "Choose your licence");
    }

    public static async Task CompleteSetupAfterLicenceAsync(
        IPage page,
        string email,
        string username,
        string org)
    {
        await ExpectVisible(page, "h1", "Create Owner Account");
        await page.Locator("#setupDisplayName").FillAsync("Rowan Smith");
        await page.Locator("#setupUsername").FillAsync(username);
        await page.Locator("#setupEmail").FillAsync(email);
        await page.Locator("#setupPassword").FillAsync("password123");
        await page.Locator("#setupPassword2").FillAsync("password123");
        await page.Locator("#setupCreateOwner").ClickAsync();

        await ExpectVisible(page, "h1", "Create your first project");
        await page.Locator("#setupProjectName").FillAsync("Platform");
        await page.Locator("#setupProjectSlug").FillAsync("platform");
        await page.Locator("#setupCreateProject").ClickAsync();

        await ExpectVisible(page, "h1", "Connect your repository");
        await page.Locator("#setupSkipRepos").ClickAsync();

        await ExpectVisible(page, "h1", "Enabled module setup");
        await page.Locator("#setupSkipModules").ClickAsync();

        await ExpectVisible(page, "h1", "Setup Complete");
        await page.Locator("#setupOpenWorkspace").ClickAsync();

        await page.WaitForFunctionAsync("() => document.getElementById('appSidebar')?.style.display !== 'none'");
        await ExpectVisible(page, "#orgLabel", org);
        await ExpectVisible(page, "#projectLabel", "Platform");
        await ExpectVisible(page, "#userHandle", $"@{username}");
    }

    public static async Task OpenLicensingAsync(IPage page)
    {
        await NavigateAsync(page, "/licensing");
        await ExpectVisible(page, "h1", "Licensing");
    }

    public static async Task SignInAsync(IPage page, string email, string password)
    {
        await ExpectVisible(page, "h1", "Sign in");
        await page.Locator("#loginEmail").FillAsync(email);
        await page.Locator("#loginPassword").FillAsync(password);
        await page.Locator("#loginSubmit").ClickAsync();
        await page.WaitForFunctionAsync("() => document.getElementById('appSidebar')?.style.display !== 'none'");
    }

    public static async Task ClickAsync(ILocator locator)
    {
        await locator.ScrollIntoViewIfNeededAsync();
        await locator.ClickAsync(new LocatorClickOptions { Force = true });
    }

    public static async Task NavigateAsync(IPage page, string route)
    {
        await ClickAsync(page.Locator($"[data-route='{route}']").First);
    }

    public static async Task OpenPeopleAsync(IPage page)
    {
        await ClickAsync(page.Locator(".sidebar-footer [data-route='/people']"));
        await ExpectVisible(page, "h1", "People");
    }

    public static async Task OpenPipelinesAsync(IPage page)
    {
        await NavigateAsync(page, "/pipelines");
        await ExpectVisible(page, "h1", "Pipelines");
    }

    public static async Task OpenRunsAsync(IPage page)
    {
        await NavigateAsync(page, "/runs");
        await ExpectVisible(page, "h1", "Runs");
    }

    public static async Task OpenRunnersAsync(IPage page)
    {
        await NavigateAsync(page, "/runners");
        await ExpectVisible(page, "h1", "Runners");
    }

    public static async Task OpenChangesAsync(IPage page)
    {
        await NavigateAsync(page, "/changes");
        await ExpectVisible(page, "h1", "Pull Requests");
    }

    public static async Task OpenQueueAsync(IPage page)
    {
        await NavigateAsync(page, "/queue");
        await ExpectVisible(page, "h1", "Review queue");
    }

    public static async Task OpenSettingsAsync(IPage page)
    {
        await NavigateAsync(page, "/settings");
        await ExpectVisible(page, "h1", "Project settings");
    }

    public static async Task OpenAuditAsync(IPage page)
    {
        await ClickAsync(page.Locator(".sidebar-footer [data-route='/audit']"));
        await ExpectVisible(page, "h1", "Audit log");
    }

    public static async Task OpenModulesAsync(IPage page)
    {
        await ClickAsync(page.Locator(".sidebar-footer [data-route='/modules']"));
        await ExpectVisible(page, "h1", "Runtime composition");
    }

    public static async Task OpenOverviewAsync(IPage page)
    {
        await NavigateAsync(page, "/overview");
        await Assertions.Expect(page.Locator("#content h1").First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await Assertions.Expect(page.Locator(".project-hero")).ToBeVisibleAsync();
    }

    public static async Task WaitForAppIdleAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => document.getElementById('app')?.getAttribute('aria-busy') === 'false'");
    }

    public static async Task ExpectVisible(IPage page, string selector, string text)
    {
        await Assertions.Expect(page.Locator(selector).Filter(new LocatorFilterOptions { HasTextString = text }).First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
    }

    public static async Task ExpectToastAsync(IPage page, string text)
    {
        await Assertions.Expect(page.Locator("#toast")).ToContainTextAsync(text, new LocatorAssertionsToContainTextOptions { Timeout = 10000 });
    }

    public static async Task CloseModalAsync(IPage page)
    {
        var modal = page.Locator("#modal[open]");
        if (await modal.CountAsync() == 0) return;
        await page.EvaluateAsync("() => document.getElementById('modal')?.close()");
        await ExpectModalClosedAsync(page);
    }

    public static async Task ExpectModalClosedAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => !document.getElementById('modal')?.open", null, new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    public static async Task ExpectModalOpenAsync(IPage page, string heading)
    {
        await Assertions.Expect(page.Locator("#modal[open] .modal-content h2")).ToContainTextAsync(heading);
    }

    public static async Task WaitForRunTerminalAsync(IPage page, int timeoutMs = 45000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var status = await page.Locator(".run-summary .list-page-header .status").First.InnerTextAsync();
            if (status is "Succeeded" or "Failed" or "Cancelled" or "PartiallySucceeded")
                return;
            await page.Locator("#refreshRun").ClickAsync();
            await page.WaitForTimeoutAsync(800);
        }
        throw new TimeoutException("Pipeline run did not reach a terminal status in time.");
    }
}
