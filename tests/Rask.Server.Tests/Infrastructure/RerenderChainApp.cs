using Rask.Core;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// A navigation that sets off a chain of renders: every OnRendered asks for one more, ten times over. Inside a
// dispatch each request only flips _pendingRenderInScope, so the coalescing loop's budget is what bounds it.
public sealed partial class RerenderChainApp : Component
{
    private readonly RouteState _routeState;
    private int _remaining;

    public RerenderChainApp(RouteState routeState) => _routeState = routeState;

    public int RendersAfterNavigation { get; private set; }

    protected override Task OnMount()
    {
        _routeState.Changed += Start;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        _routeState.Changed -= Start;
        return Task.CompletedTask;
    }

    protected override Component? HeadAssets => Markup.Title["rerender-chain"];
    protected override string? HtmlLang => null;

    protected override Component? Render()
    {
        if (_remaining > 0)
        {
            RendersAfterNavigation++;
        }

        return Markup.H1[$"path={_routeState.Path} renders={RendersAfterNavigation}"];
    }

    protected override Task OnRendered()
    {
        if (_remaining > 0 && --_remaining > 0)
        {
            StateHasChanged();
        }

        return Task.CompletedTask;
    }

    private void Start(object? sender, EventArgs e)
    {
        _remaining = 10;
        StateHasChanged();
    }
}
