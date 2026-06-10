using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

public abstract partial class SharedSmokeTests
{
    [Fact]
    public Task UserGate_SignInOut_TogglesGatedContent() => RunAsync(async () =>
    {
        await NavigateToAsync("/user");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("User & auth gating",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var gate = Page.Locator("#user-gate");
        await Expect(gate)
            .ToContainTextAsync("signed out", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Sign in as a plain user — authenticated content shows, admin panel does not.
        await gate.Locator("button:has-text('Sign in as user')").ClickAsync();
        await Expect(gate)
            .ToContainTextAsync("Signed in as", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(gate).Not.ToContainTextAsync("Admin-only panel");

        await gate.Locator("button:has-text('Sign out')").ClickAsync();
        await Expect(gate)
            .ToContainTextAsync("signed out", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Sign in as admin — now the role-gated panel appears.
        await gate.Locator("button:has-text('Sign in as admin')").ClickAsync();
        await Expect(gate).ToContainTextAsync("Admin-only panel",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });
}
