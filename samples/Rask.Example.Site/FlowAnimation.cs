using System.Globalization;

namespace Rask.Example.Site;

/// <summary>Which journey the diagram is telling. <c>null</c> cycles all of them.</summary>
internal enum FlowScene
{
    /// <summary>A click on a Server page reaching SQLite and coming back as a diff.</summary>
    ServerRoundTrip = 0,

    /// <summary>The same components moving into the browser on the next navigation.</summary>
    WasmTakeover = 1,

    /// <summary>The two kinds of island, and why only one of them is opaque.</summary>
    Islands = 2,

    /// <summary>A TypeScript SPA reaching the same handler over HTTP.</summary>
    Spa = 3,

    /// <summary>What keeps running after the UI already updated.</summary>
    Durable = 4,
}

// The architecture animation: one order placed in the Shop sample, followed from the click all the way
// to SQLite and back out as a diff that visibly changes the page -- then four more journeys over the
// same map (WASM takeover, the two island kinds, the SPA lane, and the durable work that outlives the
// request).
//
// A component built from the typed SVG family rather than a hand-written .svg file, for the reason
// RaskLogo gives: one source, no asset duplicated across wwwroots. The standalone assets/rask-flow.svg
// the README needs is BAKED from this component and compared byte-for-byte in
// tests/Rask.Example.Site.Tests -- so the file can never drift from the component.
//
// The constraints are inherited wholesale from the hero animation this replaced, and they are not
// preferences:
//
//   * Every colour is `var(--color-token, #literal)`. Rendered inline in the page the SVG inherits the
//     stylesheet's @theme tokens and follows the site's theme toggle for free; rendered standalone into
//     a file the custom properties are undefined and every colour falls back to its literal. The names
//     must match Styles/app.css exactly -- a rename leaves the diagram RENDERING, on its fallbacks,
//     silently no longer following the theme. The baked file also carries its own
//     prefers-color-scheme block, because an <img>-loaded SVG can only ever follow the OS theme.
//   * An SVG <style> inline in an HTML document is NOT scoped to the SVG -- its rules apply to the whole
//     page. Hence the `rf-` prefix on every class and keyframe name; a test enforces it.
//   * Motion is CSS keyframes. Rask.Html ships no <animate>/<animateTransform>, so SMIL is unavailable,
//     and CSS is also what survives being loaded through an <img>, which is how GitHub renders the baked
//     file.
//   * NO ids anywhere -- no <defs>, no <marker>, no <clipPath>. Inline in the page an id is global, so
//     two copies of this diagram on one page would collide. Arrowheads are polygons, drawn inline.
//
// The packets move by generated `translate()` keyframes rather than `offset-path`, and that is
// deliberate: the waypoint table that lays out an edge is the SAME table that emits the motion, so a
// packet cannot drift off the wire it is meant to be travelling. It also needs no motion-path support.
//
// Every element's BASE state is its FINAL frame -- the journey complete, the page updated -- so
// `prefers-reduced-motion: reduce` can simply turn the animations off and land on the finished picture
// rather than on a blank one.
internal sealed partial class FlowAnimation : Component
{
    // ---- canvas ----
    private const double CanvasW = 1240;
    private const double CanvasH = 800;

    // The two rails either side of the spine: what the compiler does (left) and what the operator sees
    // plus where the bytes end up (right). Neither is on the request path, which is why they are rails.
    private const double RailW = 152;
    private const double LeftRailX = 16;
    private const double RightRailX = CanvasW - RailW - LeftRailX;

    // The spine: the request path itself, top to bottom.
    private const double SpineX = 184;
    private const double SpineW = 872;

    // Band tops. A band is one horizontal stage; the packet crosses them in order.
    private const double BandFrontEnd = 56;
    private const double BandTransport = 232;
    private const double BandRender = 312;
    private const double BandApp = 404;
    private const double BandBatteries = 484;
    private const double BandData = 586;
    private const double BandStorage = 666;
    private const double LegendY = 764;

    private const double HFrontEnd = 158;
    private const double HTransport = 62;
    private const double HRender = 74;
    private const double HApp = 62;
    private const double HBatteries = 84;
    private const double HData = 62;
    private const double HStorage = 84;

    /// <summary>One journey to pin, or <c>null</c> to cycle every journey in turn.</summary>
    /// <remarks>
    ///     Null is what the hero and the baked README asset render: a self-contained loop that needs no
    ///     controls and reads the same through an <c>&lt;img&gt;</c> as it does in the page. A value is
    ///     what the site's deeper section renders behind its scenario picker, where a reader has chosen
    ///     one journey and wants it to stay put.
    /// </remarks>
    public FlowScene? Pinned { get; set; }

    protected override Component? Render()
    {
        var children = new List<Component>
        {
            SvgTitle["How a Rask app fits together, from a click in the browser down to SQLite and back"],
            Desc[Description],
            SvgStyle[Raw.Value(Css(Pinned))],
            Backdrop(),
        };

        children.AddRange(Bands());
        children.Add(BuildRail());
        children.Add(OpsRail());
        children.AddRange(NodeShapes());
        children.AddRange(Wires());
        children.AddRange(Packets());
        children.Add(Legend());

        return Svg
            .ViewBox($"0 0 {Num(CanvasW)} {Num(CanvasH)}")
            .Xmlns("http://www.w3.org/2000/svg")
            .Class(RootClass(Pinned))
            .Role("img")[children];
    }

    // The pinned variant is a class on the root rather than a different element tree, so both variants
    // bake and diff identically and the scenario picker is a one-attribute change on the wire.
    private static string RootClass(FlowScene? pinned) =>
        pinned is null
            ? "rf-svg rf-auto"
            : $"rf-svg rf-pin rf-only{Id((int)pinned.Value)}";

    // The long description is the diagram's real accessibility surface: a screen reader gets the shape of
    // the architecture in prose, not a recital of forty box labels.
    private const string Description =
        "An architecture map of a Rask application in eight horizontal bands. At the top, the front end: "
        + "Rask components written in C#, islands of React, Preact, Lit, Vue, Svelte or Blazor embedded "
        + "inside them, and a separate lane for a TypeScript SPA built with React, Preact, Vue, Solid, "
        + "Svelte, Lit or Angular. Below it the transports: a WebSocket for a server-rendered page, "
        + "JSImport and JSExport for the same components running on WebAssembly, and an HTTP endpoint "
        + "the SPA lane uses. Then the render core, where one render walk produces both HTML and a frame "
        + "stream that is diffed into edit operations, and the render ladder, which decides whether a "
        + "page is served as a static document, kept live over a socket, or moved into the browser. "
        + "Below those, application logic dispatches queries and commands; the durable batteries queue "
        + "jobs, mail, cache entries and outbox messages on the application's own database; and the data "
        + "layer writes through EF Core and SQLite into a single file. To the left, a rail of "
        + "compile-time generators and MSBuild tasks. To the right, the operator dashboard and the "
        + "backup replicas. An animated packet traces one journey at a time across the map, and the "
        + "browser window at the top left gains a new order row when the resulting diff arrives.";

    // ---- the ground the bands sit on ----
    private static Component Backdrop() =>
        G[
            Rect.X("0").Y("0").Width(Num(CanvasW)).Height(Num(CanvasH)).Class("rf-ground"),
            SvgText.X(Num(LeftRailX)).Y("30").Class("rf-t rf-lg")[
                "One codebase, one server, one database"
            ],
            SvgText
                .X(Num(CanvasW - LeftRailX))
                .Y("30")
                .TextAnchor("end")
                .Class("rf-t rf-xs rf-mut")[
                    "build time → left rail · the request path → the spine · operations → right rail"
                ]
        ];

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Pct(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Id(int v) => v.ToString(CultureInfo.InvariantCulture);
}
