using Microsoft.AspNetCore.Authorization;
using Rask.Core;
using Rask.Core.Routing;

namespace Rask.Server.Tests.Infrastructure;

// Pages that decide, as they mount or update, that the reader belongs elsewhere — "no partner chosen, go to
// the partner list" — under a layout that shows the page's title in a crumb and in the document <title>.
public sealed partial class RedirectingApp : Component
{
    protected override Component? Render() => Router;
}

[Route("/redirect")]
public sealed partial class RedirectLayout(RouteState route) : Component
{
    protected override Component? HeadAssets => Title[route.Title ?? "untitled"];

    protected override Component? Render() =>
        Div[
            Span.Id("crumb")[$"crumb:{route.Title}"],
            Button.Id("to-moved").OnClick(() => { Routes.RedirectMovedPage().Go(); })["to moved"],
            Button.Id("to-ping").OnClick(() => { Routes.RedirectPingPage().Go(); })["to ping"],
            Outlet];
}

[Route("start")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectStartPage : Component
{
    protected override string? PageTitle => "Start";

    protected override Component? Render() => P["start-content"];
}

[Route("home")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectHomePage(RouteState route) : Component
{
    protected override string? PageTitle => "Home";

    protected override Component? Render() => P[$"home-content from:{route.Query["from"]}"];
}

// Redirects as it mounts, synchronously.
[Route("moved")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectMovedPage : Component
{
    protected override string? PageTitle => "Moved";

    protected override Task OnMount()
    {
        Routes.RedirectHomePage().Go();
        return Task.CompletedTask;
    }

    protected override Component? Render() => P["moved-content"];
}

// The first link of a chain: here, then the page above, then home.
[Route("first")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectFirstPage : Component
{
    protected override Task OnMount()
    {
        Routes.RedirectMovedPage().Go();
        return Task.CompletedTask;
    }

    protected override Component? Render() => P["first-content"];
}

// Two pages that send the reader to each other.
[Route("ping")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectPingPage : Component
{
    protected override async Task OnMount()
    {
        await Task.CompletedTask;
        Routes.RedirectPongPage().Go();
    }

    protected override Component? Render() => P["ping-content"];
}

[Route("pong")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectPongPage : Component
{
    protected override async Task OnMount()
    {
        await Task.CompletedTask;
        Routes.RedirectPingPage().Go();
    }

    protected override Component? Render() => P["pong-content"];
}

// Stays mounted while its parameter changes, and redirects from OnUpdated when the record is not there.
[Route("item/{Id}")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectItemPage : Component
{
    private int _mounts;

    [RouteParam] public int Id { get; set; }

    protected override Task OnMount()
    {
        _mounts++;
        return Task.CompletedTask;
    }

    protected override Task OnUpdated()
    {
        if (Id == 0)
        {
            Routes.RedirectHomePage().Go();
        }

        return Task.CompletedTask;
    }

    protected override Component? Render() => P[$"item-{Id} mounts:{_mounts}"];
}

// Redirects to a path it only has as text, with a query.
[Route("text")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectTextPage : Component
{
    protected override Task OnMount()
    {
#pragma warning disable RASK033 // the overload that takes the path as text is what this page is for
        Go.To("/redirect/home", [KeyValuePair.Create<string, string?>("from", "text")]);
#pragma warning restore RASK033
        return Task.CompletedTask;
    }

    protected override Component? Render() => P["text-content"];
}

// Redirects after a real await, where navigation is no longer legal.
[Route("late")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectLatePage : Component
{
    private string _said = "waiting";

    protected override async Task OnMount()
    {
        await Task.Yield();
        try
        {
            Routes.RedirectHomePage().Go();
            _said = "went";
        }
        catch (InvalidOperationException)
        {
            _said = "refused";
        }
    }

    protected override Component? Render() => P[$"late-content {_said}"];
}

// Redirects into a page under a layout only a signed-in reader may open.
[Route("to-vault")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectToVaultPage : Component
{
    protected override Task OnMount()
    {
        Routes.RedirectVaultPage().Go();
        return Task.CompletedTask;
    }

    protected override Component? Render() => P["to-vault-content"];
}

[Route("/redirect-vault")]
[Authorize]
public sealed partial class RedirectVaultLayout : Component
{
    protected override Component? Render() => Div[Outlet];
}

[Route("inside")]
[ParentRoute(typeof(RedirectVaultLayout))]
public sealed partial class RedirectVaultPage : Component
{
    public static int Constructed;

    public RedirectVaultPage() => Interlocked.Increment(ref Constructed);

    protected override Component? Render() => P["vault-content"];
}

// Under the guarded layout itself: a signed-in reader with nothing chosen is sent to the page beside it.
[Route("unchosen")]
[ParentRoute(typeof(RedirectVaultLayout))]
public sealed partial class RedirectVaultUnchosenPage : Component
{
    protected override Task OnMount()
    {
        Routes.RedirectVaultPage().Go();
        return Task.CompletedTask;
    }

    protected override Component? Render() => P["unchosen-content"];
}
