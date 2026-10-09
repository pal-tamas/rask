using Rask.Core;
using Rask.Core.Routing;

namespace Rask.Wasm.Tests.Infrastructure;

// Pages that decide, as they mount or update, that the reader belongs elsewhere, under a layout that shows the
// page's title in a crumb and an App that writes it into the document <title>.
internal sealed partial class RedirectStubApp(RouteState route) : Component
{
    private static readonly IReadOnlyList<Route> _routes =
    [
        new Route(typeof(RedirectStubLayout), "/rs",
        [
            new Route(typeof(RedirectStubStart), "start"),
            new Route(typeof(RedirectStubHome), "home"),
            new Route(typeof(RedirectStubMoved), "moved"),
            new Route(typeof(RedirectStubFirst), "first"),
            new Route(typeof(RedirectStubPing), "ping"),
            new Route(typeof(RedirectStubPong), "pong"),
            new Route(typeof(RedirectStubItem), "item/{Id}"),
            new Route(typeof(RedirectStubLate), "late"),
        ]),
    ];

    protected override Component? HeadAssets => Title[route.Title ?? "untitled"];

    protected override string? HtmlLang => null;

    protected override Component? Render() => Router.Routes(_routes);
}

internal sealed partial class RedirectStubLayout(RouteState route) : Component
{
    protected override Component? Render() =>
        Div[
            Span.Id("crumb")[$"crumb:{route.Title}"],
            Button.Id("to-moved").OnClick(() => { Go.To("/rs/moved"); })["to moved"],
            Outlet];
}

internal sealed partial class RedirectStubStart : Component
{
    protected override string? PageTitle => "Start";

    protected override Component? Render() => P["start-content"];
}

internal sealed partial class RedirectStubHome : Component
{
    protected override string? PageTitle => "Home";

    protected override Component? Render() => P["home-content"];
}

internal sealed partial class RedirectStubMoved : Component
{
    protected override string? PageTitle => "Moved";

    protected override Task OnMount()
    {
        Go.To("/rs/home");
        return Task.CompletedTask;
    }

    protected override Component? Render() => P["moved-content"];
}

internal sealed partial class RedirectStubFirst : Component
{
    protected override Task OnMount()
    {
        Go.To("/rs/moved");
        return Task.CompletedTask;
    }

    protected override Component? Render() => P["first-content"];
}

internal sealed partial class RedirectStubPing : Component
{
    protected override Task OnMount()
    {
        Go.To("/rs/pong");
        return Task.CompletedTask;
    }

    protected override Component? Render() => P["ping-content"];
}

internal sealed partial class RedirectStubPong : Component
{
    protected override Task OnMount()
    {
        Go.To("/rs/ping");
        return Task.CompletedTask;
    }

    protected override Component? Render() => P["pong-content"];
}

// The record's number comes off the path, as PageTitleStubEdit's does. OnUpdated runs when the page mounts; a
// later change of address is seen where the page renders, which is where it redirects from.
internal sealed partial class RedirectStubItem(RouteState route) : Component
{
    private string Id => route.Path[(route.Path.LastIndexOf('/') + 1)..];

    protected override Task OnUpdated()
    {
        SendOnWhenMissing();
        return Task.CompletedTask;
    }

    protected override Component? Render()
    {
        SendOnWhenMissing();
        return P[$"item-{Id}"];
    }

    private void SendOnWhenMissing()
    {
        if (Id == "0" && route.Path.StartsWith("/rs/item/", StringComparison.Ordinal))
        {
            Go.To("/rs/home");
        }
    }
}

internal sealed partial class RedirectStubLate : Component
{
    private string _said = "waiting";

    protected override async Task OnMount()
    {
        await Task.Yield();
        try
        {
            Go.To("/rs/home");
            _said = "went";
        }
        catch (InvalidOperationException)
        {
            _said = "refused";
        }
    }

    protected override Component? Render() => P[$"late-content {_said}"];
}
