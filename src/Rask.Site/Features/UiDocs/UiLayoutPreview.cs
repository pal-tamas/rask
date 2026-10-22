using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     A layout example's result: its demo page in a frame, and Flux's Fullscreen button that opens it.
/// </summary>
/// <remarks>
///     <para>
///     A layout is a whole document: the sidebar's states are page-wide ids, and on a phone it slides over the
///     viewport, so it cannot sit in a box of the docs page. The frame holds the demo's own page, prerendered so the
///     layout is drawn before its runtime loads, and lazy so a frame far down the page costs nothing yet.
///     </para>
///     <para>
///     From 640px of column up the frame is a desktop at least 1280px wide, scaled down to the column; narrower, it
///     is the column's own width, so a phone shows the layout a phone gets.
///     </para>
/// </remarks>
internal sealed partial class UiLayoutPreview : Component
{
    // Each step scales by s and widens the frame to 100/s of the column, so it always fills the column exactly.
    private const string Box =
        "relative h-[560px] @min-[640px]:h-[400px] @min-[720px]:h-[450px] @min-[800px]:h-[500px] @min-[880px]:h-[550px]";

    private const string Frame =
        "block h-full w-full border-0 @min-[640px]:absolute @min-[640px]:top-0 @min-[640px]:left-0 "
        + "@min-[640px]:h-[800px] @min-[640px]:origin-top-left "
        + "@min-[640px]:w-[200cqw] @min-[640px]:scale-50 "
        + "@min-[720px]:w-[177.78cqw] @min-[720px]:scale-[.5625] "
        + "@min-[800px]:w-[160cqw] @min-[800px]:scale-[.625] "
        + "@min-[880px]:w-[145.45cqw] @min-[880px]:scale-[.6875]";

    /// <summary>The demo page.</summary>
    public required RouteUrl Demo { get; set; }

    /// <summary>What the frame is called to a screen reader.</summary>
    public required string Title { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Div.Class("@container overflow-hidden rounded-lg border border-zinc-200 dark:border-zinc-700")[
                Div.Class(Box)[
                    Iframe.Src(PageMeta.LinkTo(Demo)).Title(Title).Loading(Loading.Lazy).Class(Frame)
                ]
            ],
            Div.Class("flex justify-end")[
                Ui.Button.Sm.Icon(Ui.IconName.ArrowsPointingOut).Href(PageMeta.LinkTo(Demo))["Fullscreen"]
            ]
        ];
}
