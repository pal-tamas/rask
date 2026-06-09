using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

public abstract partial class SharedSmokeTests
{
    // The headless Authorize component (declarative gate) on the /user page. Its three slots —
    // Authorized / NotAuthorized / Authorizing — must reflect the signed-in principal as the shared
    // DemoUserProvider is toggled, with no page reload (live re-render via IUserProvider.Changed).
    [Fact]
    public Task AuthorizeComponent_Slots_ReflectRole() => RunAsync(async () =>
    {
        await NavigateToAsync("/user");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("User & auth gating",
            new() { Timeout = 30_000 });

        var demo = Page.Locator("#authorize-demo");

        // Anonymous → the NotAuthorized fallback slot.
        await Expect(demo).ToContainTextAsync("Sign in to see member content", new() { Timeout = 10_000 });
        await Expect(demo).Not.ToContainTextAsync("Admin-only content");

        // Plain user → the authenticated ("standard access") slot, but not the admin slot.
        await demo.Locator("button:has-text('Sign in as user')").ClickAsync();
        await Expect(demo).ToContainTextAsync("standard access", new() { Timeout = 10_000 });
        await Expect(demo).Not.ToContainTextAsync("Admin-only content");

        // Sign out → back to the fallback slot.
        await demo.Locator("button:has-text('Sign out')").ClickAsync();
        await Expect(demo).ToContainTextAsync("Sign in to see member content", new() { Timeout = 10_000 });

        // Admin → the role-gated Authorized slot.
        await demo.Locator("button:has-text('Sign in as admin')").ClickAsync();
        await Expect(demo).ToContainTextAsync("Admin-only content", new() { Timeout = 10_000 });
        await Expect(demo).Not.ToContainTextAsync("Sign in to see member content");
    });
}
