using Microsoft.Playwright;

namespace E2E.Playwright.Tests;

internal static class Ui
{
    public static async Task CompleteSetupAsync(IPage page, string org = "Northstar Engineering", string email = "rowan@example.com", string username = "rowan", bool installDefaultModules = true)
    {
        await CompleteSetupThroughLicenceAsync(page, org);
        await page.Locator("#setupUseCommunity").ClickAsync();
        await CompleteSetupAfterLicenceAsync(page, email, username, org, installDefaultModules);
    }

    public static async Task CompleteSetupWithCommercialLicenceAsync(
        IPage page,
        string licenceJson,
        string org = "Northstar Engineering",
        string email = "rowan@example.com",
        string username = "rowan")
    {
        await CompleteSetupThroughLicenceAsync(page, org);
        await page.Locator("#setupLicencePayload").EvaluateAsync("(el, value) => { el.value = value; }", licenceJson);
        await page.Locator("#setupValidateLicence").ClickAsync();
        await ExpectVisible(page, "h1", "Choose capabilities");
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
        string org,
        bool installDefaultModules = true)
    {
        await ExpectVisible(page, "h1", "Choose capabilities");
        if (installDefaultModules)
        {
            await page.Locator("#setupInstallModules").ClickAsync();
        }
        else
        {
            await page.Locator("#setupSkipModules").ClickAsync();
        }

        await ExpectVisible(page, "h1", "Create Owner Account");
        await page.Locator("#setupDisplayName").FillAsync("Rowan Smith");
        await page.Locator("#setupUsername").FillAsync(username);
        await page.Locator("#setupEmail").FillAsync(email);
        await page.Locator("#setupPassword").FillAsync("password123");
        await page.Locator("#setupPassword2").FillAsync("password123");
        await page.Locator("#setupCreateOwner").ClickAsync();

        await ExpectVisible(page, "h1", $"{org} is ready");
        await page.Locator("#setupOpenWorkspace").ClickAsync();

        await page.WaitForFunctionAsync("() => document.getElementById('appSidebar')?.style.display !== 'none'");
        await ExpectVisible(page, "#orgLabel", org);
        await Assertions.Expect(page.Locator("#userHandle")).ToHaveTextAsync($"@{username}");

        // Most feature tests need a project context; create the default Platform project via API.
        await EnsureDefaultProjectAsync(page, "Platform", "platform");
    }

    public static async Task EnsureDefaultProjectAsync(IPage page, string name = "Platform", string slug = "platform")
    {
        await page.EvaluateAsync("""
            async ({ name, slug }) => {
              const token = localStorage.getItem('forgedeck.token');
              const headers = {
                'Content-Type': 'application/json',
                ...(token ? { Authorization: `Bearer ${token}` } : {})
              };
              const existing = await fetch('/api/projects', { headers }).then(r => r.json());
              let project = (existing || []).find(p => p.slug === slug);
              if (!project) {
                const response = await fetch('/api/projects', {
                  method: 'POST',
                  headers,
                  body: JSON.stringify({
                    name,
                    slug,
                    description: 'Modular software delivery platform',
                    visibility: 'Private',
                    repositoryMode: 'SingleRepository'
                  })
                });
                if (!response.ok) {
                  const body = await response.text();
                  throw new Error(`Create project failed: ${response.status} ${body}`);
                }
                project = await response.json();
              }
              await fetch('/api/core/context/project', {
                method: 'POST',
                headers,
                body: JSON.stringify({ projectId: project.id })
              });
              if (typeof loadWorkspace === 'function') await loadWorkspace();
            }
            """, new { name, slug });
        await page.WaitForFunctionAsync("() => document.getElementById('appSidebar')?.style.display !== 'none'");
        await ExpectVisible(page, "#projectLabel", name);
    }

    public static async Task OpenLicensingAsync(IPage page)
    {
        await EnsureOrgNavAsync(page);
        await GoToHashAsync(page, "/organisation/settings/license");
        await ExpectVisible(page, "h1", "License");
    }

    public static async Task EnsureOrgNavAsync(IPage page)
    {
        if (await page.Locator("#primaryNav [data-route='/home']").CountAsync() > 0)
        {
            return;
        }

        await page.EvaluateAsync("() => location.hash = '#/home'");
        await page.WaitForFunctionAsync("() => document.querySelector(\"#primaryNav [data-route='/home']\")");
    }

    public static async Task EnsureProjectNavAsync(IPage page)
    {
        if (await page.Locator("#primaryNav [data-route='/overview']").CountAsync() > 0)
        {
            return;
        }

        await page.EvaluateAsync("() => location.hash = '#/overview'");
        await page.WaitForFunctionAsync("() => document.querySelector(\"#primaryNav [data-route='/overview']\")");
    }

    public static async Task GoToHashAsync(IPage page, string route)
    {
        var path = route.StartsWith('#') ? route[1..] : route;
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        await page.EvaluateAsync("(value) => { location.hash = value; }", path);
        await page.WaitForFunctionAsync(
            @"(expected) => {
                const hash = (location.hash || '').replace(/^#/, '');
                return hash === expected || hash === expected.replace(/\/$/, '');
            }",
            path);
        await page.Locator("#content h1").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await WaitForAppIdleAsync(page);
    }

    public static async Task SignInAsync(IPage page, string email, string password)
    {
        await ExpectVisible(page, "h1", "Sign in");
        await page.Locator("#loginEmail").FillAsync(email);
        await page.Locator("#loginPassword").FillAsync(password);
        await page.Locator("#loginSubmit").ClickAsync();
        await page.WaitForFunctionAsync("() => document.getElementById('appSidebar')?.style.display !== 'none'");
    }

    /// <summary>
    /// Clicks through a re-rendering SPA: pages like the pipeline builder replace #content wholesale, so a
    /// node resolved for the scroll step can be detached before the click lands. Re-resolve and retry.
    /// </summary>
    public static async Task ClickAsync(ILocator locator)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await locator.ScrollIntoViewIfNeededAsync();
                await locator.ClickAsync(new LocatorClickOptions { Force = true });
                return;
            }
            catch (PlaywrightException ex) when (attempt < 5 && ex.Message.Contains("not attached", StringComparison.Ordinal))
            {
                await locator.Page.WaitForTimeoutAsync(250);
            }
        }
    }

    public static async Task NavigateAsync(IPage page, string route)
    {
        await ClickAsync(page.Locator($"[data-route='{route}']").First);
    }

    public static async Task OpenPeopleAsync(IPage page)
    {
        await EnsureOrgNavAsync(page);
        await NavigateAsync(page, "/people");
        await ExpectVisible(page, "h1", "People");
    }

    public static async Task OpenAuditAsync(IPage page)
    {
        await EnsureOrgNavAsync(page);
        await GoToHashAsync(page, "/organisation/settings/audit");
        await ExpectVisible(page, "h1", "Audit log");
    }

    public static async Task OpenModulesAsync(IPage page)
    {
        await EnsureOrgNavAsync(page);
        await GoToHashAsync(page, "/organisation/settings/modules");
        await ExpectVisible(page, "h1", "Modules");
    }

    /// <summary>Installs bundled product modules after a Core-only setup (for tests that exercise Review/Build).</summary>
    public static async Task InstallBundledModulesAsync(IPage page, params string[] extensionIds)
    {
        var ids = extensionIds.Length == 0
            ? ["forgedeck.code", "forgedeck.review", "forgedeck.build"]
            : extensionIds;
        foreach (var id in ids)
        {
            await page.EvaluateAsync("""
                async (extensionId) => {
                  const token = localStorage.getItem('forgedeck.token');
                  const response = await fetch(`/api/platform/extensions/${encodeURIComponent(extensionId)}/install`, {
                    method: 'POST',
                    headers: {
                      'Content-Type': 'application/json',
                      ...(token ? { Authorization: `Bearer ${token}` } : {})
                    },
                    body: JSON.stringify({ enable: true })
                  });
                  if (!response.ok) {
                    const body = await response.text();
                    throw new Error(`Install ${extensionId} failed: ${response.status} ${body}`);
                  }
                }
                """, id);
        }
        await page.EvaluateAsync("""
            async () => {
              if (typeof reloadComposition === 'function') await reloadComposition();
              else if (typeof loadModules === 'function') await loadModules();
            }
            """);
        await WaitForAppIdleAsync(page);
    }

    public static async Task OpenPipelinesAsync(IPage page)
    {
        await EnsureProjectNavAsync(page);
        await NavigateAsync(page, "/pipelines");
        await ExpectVisible(page, "h1", "Build");
    }

    public static async Task OpenEnvironmentsAsync(IPage page)
    {
        await EnsureProjectNavAsync(page);
        await NavigateAsync(page, "/environments");
        await ExpectVisible(page, "h1", "Environments");
    }

    public static async Task OpenDeploymentsAsync(IPage page)
    {
        await EnsureProjectNavAsync(page);
        await NavigateAsync(page, "/deployments");
        await ExpectVisible(page, "h1", "Deployments");
    }

    public static async Task OpenRunsAsync(IPage page)
    {
        await EnsureProjectNavAsync(page);
        await NavigateAsync(page, "/runs");
        await ExpectVisible(page, "h1", "Runs");
    }

    public static async Task OpenRunnersAsync(IPage page)
    {
        await EnsureOrgNavAsync(page);
        await GoToHashAsync(page, "/organisation/settings/build");
        await ExpectVisible(page, "h1", "Runners");
        await Assertions.Expect(page.Locator("#addRunner")).ToBeVisibleAsync();
    }

    public static async Task OpenChangesAsync(IPage page)
    {
        await EnsureProjectNavAsync(page);
        await NavigateAsync(page, "/changes");
        await ExpectVisible(page, "h1", "Pull Requests");
    }

    public static async Task OpenQueueAsync(IPage page)
    {
        await EnsureProjectNavAsync(page);
        await NavigateAsync(page, "/queue");
        await ExpectVisible(page, "h1", "Review queue");
    }

    public static async Task OpenSettingsAsync(IPage page)
    {
        await EnsureProjectNavAsync(page);
        await GoToHashAsync(page, "/settings/repositories");
        await ExpectVisible(page, "h1", "Project settings");
    }

    public static async Task OpenOverviewAsync(IPage page)
    {
        await EnsureProjectNavAsync(page);
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
        if (await modal.CountAsync() == 0)
        {
            return;
        }

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
            {
                return;
            }

            await page.Locator("#refreshRun").ClickAsync();
            await page.WaitForTimeoutAsync(800);
        }
        throw new TimeoutException("Pipeline run did not reach a terminal status in time.");
    }
}
