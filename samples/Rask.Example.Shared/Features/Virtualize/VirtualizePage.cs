using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("virtualize")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class VirtualizePage : Component
{
    protected override RenderResult Head => Title()["Virtualize — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Virtualize",
            $"Headless list virtualization. Each list below holds {VirtualizeData.Rows.Length:N0} rows, but the DOM only ever keeps the visible window plus a small overscan."),

        P(Class: "small text-secondary mb-4")[
            "Scroll a box below. Two keyed spacer rows (",
            Code()["OffsetBefore"], " and ", Code()["OffsetAfter"],
            ") inside the ", Code()["tbody"], " reserve the scroll height of the off-screen rows while ",
            Code()["VisibleItems"], " only emits the rows currently on screen. Keeping them in the table — ",
            "so its outer height stays constant — lets the sticky header stay pinned instead of unsticking ",
            "as the windowed rows re-render under it."
        ],

        H2(Class: "h4 mt-4 mb-3")["In-memory rows via Items"],
        CodeSample(
            ["VirtualizeItemsDemo.cs"],
            Notes:
            "10,000 rows go in, ~20–30 stay in the DOM. data-row-index on each row lets you eyeball the rendered slice in DevTools; data-rask-key gives every row a stable identity so scrolling moves nodes instead of replacing them.",
            Result: VirtualizeItemsDemo()),

        H2(Class: "h4 mt-5 mb-3")["Async paging via ItemsProvider"],
        CodeSample(
            ["VirtualizeProviderDemo.cs"],
            Notes:
            "The same component, backed by a provider that simulates a 350 ms API call per window. Visible rows show a — placeholder until the fetch resolves, then morph in. Navigating away mid-fetch cancels the in-flight call via req.CancellationToken.",
            Result: VirtualizeProviderDemo())
    ];
}
