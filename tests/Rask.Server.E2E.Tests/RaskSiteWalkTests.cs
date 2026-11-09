using System.Collections.Concurrent;
using Microsoft.Playwright;
using Rask.Testing;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     Rask.Testing.Playwright's <see cref="RaskSite{TProgram}" /> over a real app: Rask.Testing.Fixture.App, referenced the
///     way an app's own E2E project references its app, started as a process of its own, its pages walked in Chromium.
/// </summary>
/// <remarks>
///     The pages are named rather than listed with <c>RaskSite.Pages&lt;Program&gt;()</c>: listing them runs the fixture's
///     route registry, which would register its pages into this process's route table, where this suite's own hosts
///     route. Rask.Testing.Playwright.Tests lists them, in a process of its own.
/// </remarks>
public sealed class RaskSiteWalkTests(RaskSiteWalkTests.FixtureSite site) : IClassFixture<RaskSiteWalkTests.FixtureSite>
{
    // /wide fails on purpose (below). /admin is behind [Authorize] on an app with Rask.Auth off: there for no one.
    public static TheoryData<string> Pages() => ["/", "/login", "/save"];

    [Theory]
    [MemberData(nameof(Pages))]
    public Task Every_page_of_the_app_opens_whole_and_fits_a_phone(string path) => site.Walk(path);

    [Fact]
    public async Task A_page_wider_than_a_phone_fails_its_walk_naming_what_does_not_fit()
    {
        var walk = site.Walk("/wide");

        var failed = await Assert.ThrowsAsync<WalkException>(walk.ToTask);

        var problem = Assert.Single(failed.Problems);
        Assert.Contains("wider than a phone", problem, StringComparison.Ordinal);
        Assert.Contains("table#orders", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_is_not_there_fails_its_walk_as_not_found()
    {
        var walk = site.Walk("/nowhere");

        var failed = await Assert.ThrowsAsync<WalkException>(walk.ToTask);

        Assert.Equal(["GET /nowhere answered 404 Not Found"], failed.Problems);
    }

    [Fact]
    public async Task A_person_signs_in_once_and_every_later_page_of_theirs_starts_signed_in()
    {
        var first = await site.SignedIn("ann");
        var second = await site.SignedIn("ann");

        var cookies = await second.Context.CookiesAsync();
        Assert.Contains(cookies, cookie => cookie is { Name: "who", Value: "ann" });
        Assert.Equal(1, site.SignInsOf("ann"));
        await first.Context.CloseAsync();
        await second.Context.CloseAsync();
    }

    [Fact]
    public async Task Pages_asked_for_one_person_at_once_share_one_sign_in()
    {
        var pages = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => site.SignedIn("carol")));

        Assert.Equal(1, site.SignInsOf("carol"));
        foreach (var page in pages)
        {
            await page.Context.CloseAsync();
        }
    }

    [Fact]
    public async Task A_walk_as_a_person_opens_the_page_signed_in_as_them()
    {
        await site.Walk("/save").As("bob");

        Assert.Equal(1, site.SignInsOf("bob"));
    }

    /// <summary>The fixture app, signing a person in its own way: a cookie naming them, set on the sign-in page.</summary>
    public sealed class FixtureSite : RaskSite<global::Program>
    {
        private readonly ConcurrentDictionary<string, int> _signIns = new(StringComparer.Ordinal);

        public FixtureSite() => SignIn = async (page, name) =>
        {
            await page.GotoAsync("/login");
            await page.Context.AddCookiesAsync([new Cookie { Name = "who", Value = name, Url = Address.ToString() }]);
            _signIns.AddOrUpdate(name, 1, (_, count) => count + 1);
        };

        public int SignInsOf(string name) => _signIns.GetValueOrDefault(name);
    }
}
