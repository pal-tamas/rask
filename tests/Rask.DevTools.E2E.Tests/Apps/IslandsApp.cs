using Rask;
using Rask.Blazor;
using Rask.Blazor.Library.Fixture;
using Rask.Core;
using Rask.DevTools.E2E.Tests.Apps.Islands;

namespace Rask.DevTools.E2E.Tests.Apps;

/// <summary>
///     The page the island journeys inspect: a working Lit island, one whose mount fails, and a real Blazor component
///     whose own <c>@onclick</c> changes the page.
/// </summary>
public sealed partial class IslandsApp : Component
{
    private string _picked = "none";

    protected override Component? HeadAssets =>
    [
        Title["DevTools E2E islands"],
        Meta.Charset("utf-8"),
        Style[Raw.Value(UiStylesheet.Css)],
    ];

    protected override Component? Render() =>
        Main[
            Section.Id("lit")[DevToolsMeter.Label("CPU").Value(42)],
            Section.Id("broken")[DevToolsMeter.Label("Disk").Broken(true)],
            Section.Id("blazor")[DevToolsTicker.Symbol("RASK").Price(12.5m).OnPick(symbol => _picked = symbol)],
            P.Id("picked")[$"picked={_picked}"]
        ];
}

/// <summary>A component from a real Razor class library, hosted as a Rask component. Body deliberately empty.</summary>
public sealed partial class DevToolsTicker : BlazorComponent<Ticker>;
