using Microsoft.AspNetCore.Authorization;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Server.Tests.Infrastructure;

// Pages that decide, as they mount or update, that the reader belongs elsewhere — "no partner chosen, go to
// the partner list" — under a layout that shows the page's title in a crumb and in the document <title>.
public sealed partial class RedirectingApp : Component
{
    protected override Component? Render() => Router;
}

[Route("/redirect")]
public sealed partial class RedirectLayout(RouteState route, IPersistentState state) : Component
{
    protected override Component? HeadAssets => Title[route.Title ?? "untitled"];

    protected override Component? Render() =>
        Div[
            Span.Id("crumb")[$"crumb:{route.Title}"],
            Span.Id("kept")[$"kept:{(state.TryGet<int>("kept", out var kept) ? kept : 0)}"],
            Button.Id("keep").OnClick(() => state.Persist("kept", 1))["keep"],
            Button.Id("to-moved").OnClick(() => { Routes.RedirectMovedPage().Go(); })["to moved"],
            Button.Id("to-ping").OnClick(() => { Routes.RedirectPingPage().Go(); })["to ping"],
            Button.Id("to-late").OnClick(() => { Routes.RedirectLatePage().Go(); })["to late"],
            Button.Id("out").OnClick(() => Go.Out("/tenants"))["out"],
            Button.Id("save-then-late").OnClick(SaveThenGo)["save"],
            Outlet];

    // The documented save, landing on a page that loads and then sends the reader on.
    private static async Task SaveThenGo()
    {
        await Task.Delay(1).ConfigureAwait(false);
        Routes.RedirectLatePage().Go();
    }
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

// Loads, finds nothing, and sends the reader on: "record not found, back to the list".
[Route("late")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectLatePage : Component
{
    protected override async Task OnMount()
    {
        await Task.Yield();
        Routes.RedirectHomePage().Go();
    }

    protected override Component? Render() => P["late-content"];
}

// The same, with a load that takes as long as the test says.
[Route("gated")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectGatedPage : Component
{
    protected override async Task OnMount()
    {
        await RedirectGate.Opened;
        try
        {
            Routes.RedirectHomePage().Go();
        }
        finally
        {
            RedirectGate.Asked();
        }
    }

    protected override Component? Render() => P["gated-content"];
}

// What the gated page's load waits for, and the moment after it has asked to go.
public static class RedirectGate
{
    private static TaskCompletionSource _opened = New();
    private static TaskCompletionSource _asked = New();

    public static Task Opened => _opened.Task;

    public static Task HasAsked => _asked.Task;

    public static void Reset()
    {
        _opened = New();
        _asked = New();
    }

    public static void Open() => _opened.TrySetResult();

    public static void Asked() => _asked.TrySetResult();

    private static TaskCompletionSource New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

// Loads, then sends the reader into the guarded layout.
[Route("late-vault")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectLateVaultPage : Component
{
    protected override async Task OnMount()
    {
        await Task.Yield();
        Routes.RedirectVaultPage().Go();
    }

    protected override Component? Render() => P["late-vault-content"];
}

// Two pages that each load and then send the reader to the other.
[Route("slow-ping")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectSlowPingPage : Component
{
    protected override async Task OnMount()
    {
        await Task.Yield();
        Routes.RedirectSlowPongPage().Go();
    }

    protected override Component? Render() => P["slow-ping-content"];
}

[Route("slow-pong")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectSlowPongPage : Component
{
    protected override async Task OnMount()
    {
        await Task.Yield();
        Routes.RedirectSlowPingPage().Go();
    }

    protected override Component? Render() => P["slow-pong-content"];
}

// What the session has chosen to work with; a session rebuilt after a restart has chosen nothing.
public sealed class RedirectChoice
{
    public bool Chosen { get; set; }
}

// Needs a choice, and sends a reader who has made none to where one is made.
[Route("work")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectWorkPage(RedirectChoice choice) : Component
{
    protected override Task OnMount()
    {
        if (!choice.Chosen)
        {
            Routes.RedirectHomePage().Go();
        }

        return Task.CompletedTask;
    }

    protected override Component? Render() => P["work-content"];
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

// Sends the reader to a page of the site this app does not render, as it mounts.
[Route("out")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectOutPage : Component
{
    protected override Task OnMount()
    {
        Go.Out("/tenants");
        return Task.CompletedTask;
    }

    protected override Component? Render() => P["out-content"];
}

// The same, after a load.
[Route("late-out")]
[ParentRoute(typeof(RedirectLayout))]
public sealed partial class RedirectLateOutPage : Component
{
    protected override async Task OnMount()
    {
        await Task.Yield();
        Go.Out("/tenants");
    }

    protected override Component? Render() => P["late-out-content"];
}
