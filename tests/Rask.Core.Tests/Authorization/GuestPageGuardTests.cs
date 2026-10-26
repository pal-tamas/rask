using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authorization;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Authorization;

// [Guest]: a sign-in page a reader already signed in is sent past, before it is built.
public sealed class GuestPageGuardTests
{
    [Fact]
    public async Task A_signed_in_reader_is_sent_home_from_a_guest_page()
    {
        var services = Services();

        var result = await RouteAuthorizationGuard.Evaluate(services, [typeof(SignInPage)], SignedIn());

        Assert.Equal(RouteAuthorizationOutcome.Redirect, result.Outcome);
        Assert.Equal("/", result.RedirectTo);
    }

    [Fact]
    public async Task A_reader_who_is_not_signed_in_opens_a_guest_page()
    {
        var services = Services();

        var result = await RouteAuthorizationGuard.Evaluate(services, [typeof(SignInPage)], Anonymous());

        Assert.Equal(RouteAuthorizationOutcome.Allow, result.Outcome);
    }

    [Fact]
    public async Task A_signed_in_reader_goes_to_the_page_the_attribute_names()
    {
        var services = Services();

        var result = await RouteAuthorizationGuard.Evaluate(services, [typeof(RegisterPage)], SignedIn());

        Assert.Equal("/app?welcome=back", result.RedirectTo);
    }

    [Fact]
    public async Task A_guest_layout_covers_its_pages_and_a_page_of_its_own_says_where_instead()
    {
        var services = Services();

        var underLayout = await RouteAuthorizationGuard.Evaluate(services, [typeof(GuestLayout), typeof(PlainPage)], SignedIn());
        var ownGuest = await RouteAuthorizationGuard.Evaluate(services, [typeof(GuestLayout), typeof(RegisterPage)], SignedIn());

        Assert.Equal("/layout-home", underLayout.RedirectTo);
        Assert.Equal("/app?welcome=back", ownGuest.RedirectTo);
    }

    [Fact]
    public async Task A_guest_page_under_a_guarded_layout_still_challenges_a_reader_who_is_not_signed_in()
    {
        var services = Services();

        var result = await RouteAuthorizationGuard.Evaluate(services, [typeof(GuardedLayout), typeof(SignInPage)], Anonymous());

        Assert.Equal(RouteAuthorizationOutcome.Challenge, result.Outcome);
    }

    [Theory]
    [InlineData(typeof(ToAnotherSitePage))]
    [InlineData(typeof(ProtocolRelativePage))]
    public async Task A_destination_that_could_lead_off_the_site_is_refused_with_the_page_named(Type page)
    {
        var services = Services();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RouteAuthorizationGuard.Evaluate(services, [page], SignedIn()));

        Assert.Contains(page.Name, refused.Message, StringComparison.Ordinal);
    }

    private static IServiceProvider Services() =>
        new ServiceCollection().AddLogging().AddAuthorizationCore().BuildServiceProvider();

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal SignedIn() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "TestAuth"));

    [Guest]
    private sealed class SignInPage : Component
    {
        protected override Component? Render() => this;
    }

    [Guest(Then = "/app?welcome=back")]
    private sealed class RegisterPage : Component
    {
        protected override Component? Render() => this;
    }

    [Guest(Then = "/layout-home")]
    private sealed class GuestLayout : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class PlainPage : Component
    {
        protected override Component? Render() => this;
    }

    [Authorize]
    private sealed class GuardedLayout : Component
    {
        protected override Component? Render() => this;
    }

    [Guest(Then = "https://elsewhere.example/")]
    private sealed class ToAnotherSitePage : Component
    {
        protected override Component? Render() => this;
    }

    [Guest(Then = "//elsewhere.example/")]
    private sealed class ProtocolRelativePage : Component
    {
        protected override Component? Render() => this;
    }
}
