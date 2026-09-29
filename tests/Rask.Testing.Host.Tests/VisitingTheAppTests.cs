using System.Security.Claims;
using Rask.Testing.Fixture.App;

namespace Rask.Testing.Host.Tests;

public sealed class VisitingTheAppTests
{
    private static readonly ClaimsPrincipal Admin = new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, "ann@example.com"), new Claim(ClaimTypes.Role, "Admin")], "Test"));

    private static readonly ClaimsPrincipal Visitor = new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, "bob@example.com")], "Test"));

    [Fact]
    public void A_visit_runs_the_apps_own_Program_cs()
    {
        var page = Page.Visit("/");

        page.Shows("Hello from Program.cs");
    }

    [Fact]
    public void What_a_test_does_before_its_visit_happens_in_the_app_it_visits()
    {
        Guestbook().Names.Add("Ann");

        var page = Page.Visit("/");

        page.Shows("1 signed the guestbook");
    }

    [Fact]
    public void Each_test_gets_an_app_of_its_own()
    {
        var page = Page.Visit("/");

        page.Shows("0 signed the guestbook");
    }

    [Fact]
    public async Task A_toast_the_page_raises_is_on_screen()
    {
        var page = Page.Visit("/save");

        await page.Click("Save");

        page.Shows("Saved");
    }

    [Fact]
    public void A_page_behind_a_role_sends_someone_signed_out_to_sign_in()
    {
        var page = Page.Visit("/admin");

        page.IsAt("/login");
        page.Shows("Sign in");
    }

    [Fact]
    public void A_page_behind_a_role_opens_for_someone_in_that_role()
    {
        var page = Page.Visit("/admin").As(Admin);

        page.IsAt("/admin");
        page.Shows("Only admins");
    }

    [Fact]
    public void A_page_behind_a_role_is_forbidden_to_someone_without_it()
    {
        var page = Page.Visit("/admin").As(Visitor);

        page.IsAt("/forbidden");
    }

    [Fact]
    public void Signing_in_is_only_for_a_page_opened_on_an_app()
    {
        var page = Page.Render(() => null);

        var error = Assert.Throws<InvalidOperationException>(() => page.As(Admin));

        Assert.Contains("Page.Visit", error.Message, StringComparison.Ordinal);
    }

    // What a battery's static call does before any page is open: reach the services of the test's app.
    private static Guestbook Guestbook() =>
        (Guestbook)(Ambient.Services?.GetService(typeof(Guestbook))
                    ?? throw new InvalidOperationException("No app is open for this test."));
}
