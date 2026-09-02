namespace Rask.Example.Site;

// The journeys. Each is a list of waypoints; the same list draws nothing and animates everything, so a
// packet can never drift off the route it is meant to be describing.
//
// Waypoints are box CENTRES, computed from FlowAnimation.Nodes.cs rather than typed in twice -- moving a
// box in the table moves the packet with it.
internal sealed partial class FlowAnimation
{
    private readonly record struct Wp(double X, double Y);

    /// <param name="Path">The main packet's route.</param>
    /// <param name="Second">A second packet, where the point of the scene is a contrast.</param>
    /// <param name="LandsRow">Whether this journey ends with the page gaining a row.</param>
    private sealed record SceneDef(
        string Title,
        string Caption,
        Wp[] Path,
        Wp[] Second,
        bool LandsRow);

    // Centre of a node, by key. Throws rather than defaulting: a typo in a route should stop the build,
    // not silently park a packet at the origin.
    private static Wp At(string key)
    {
        var node = AllNodes().Single(n => n.Key == key);
        return new Wp(node.X + (node.W / 2), node.Y + (node.H / 2));
    }

    private static Wp Xy(double x, double y) => new(x, y);

    // The "+ New" button inside the browser window, where every front-end journey starts. Derived from
    // the node rather than typed in, so moving the window moves the route with it.
    private static Wp Click()
    {
        var screen = AllNodes().Single(n => n.Key == "browser");
        return new Wp(screen.X + screen.W - 43, screen.Y + 38);
    }

    // Where a returning diff lands: the new row.
    private static Wp Landing()
    {
        var screen = AllNodes().Single(n => n.Key == "browser");
        return new Wp(screen.X + (screen.W / 2), screen.Y + 115);
    }

    private static readonly SceneDef[] SceneDefs =
    [
        // 0 -- the whole point of the framework, in one round trip.
        new(
            "A click reaches SQLite and comes back as a diff",
            "handler id → /rask/ws → IDispatcher → EF → app.db, then EditOp[] back over the same socket",
            [
                Click(),
                Xy(400, 222),
                At("ws"),
                Xy(400, 300),
                At("cqrs"),
                At("handlers"),
                At("outbox"),
                At("data"),
                At("ef"),
                At("sqliteef"),
                At("sqlite"),
                Xy(620, 656),
                At("appdb"),
                Xy(400, 660),
                At("serialize"),
                At("frames"),
                At("differ"),
                At("payload"),
                At("ws"),
                Landing(),
            ],
            [],
            LandsRow: true),

        // 1 -- the same components, one band shorter. The lesson is the hop that ISN'T there.
        new(
            "The same components move into the browser",
            "the bundle arrives at idle, the socket closes 1000 \"handoff\", and the render happens in the tab",
            [
                Click(),
                At("jsi"),
                At("cqrs"),
                At("browserdb"),
                At("cqrs"),
                At("serialize"),
                At("frames"),
                At("differ"),
                At("payload"),
                At("jsi"),
                Landing(),
            ],
            [],
            LandsRow: true),

        // 2 -- two islands, opposite diff semantics. The two packets are the argument.
        new(
            "Two kinds of island, opposite diff semantics",
            "React owns its DOM, so the subtree is opaque and one attribute op reaches adapter.update — Blazor is rendered by Rask, so it is diffed like any other markup",
            [
                Xy(670, 138),
                At("ws"),
                At("cqrs"),
                At("handlers"),
                At("cqrs"),
                At("payload"),
                At("ws"),
                Xy(670, 138),
            ],
            [
                Xy(1000, 138),
                At("ws"),
                At("cqrs"),
                At("handlers"),
                At("cqrs"),
                At("serialize"),
                At("frames"),
                At("differ"),
                At("payload"),
                At("ws"),
                Xy(1000, 138),
            ],
            LandsRow: true),

        // 3 -- the parallel lane. Note which bands it never touches.
        new(
            "A TypeScript SPA reaches the same handler",
            "no Component, no session, no diff — generated TypeScript straight to the CQRS endpoint",
            [
                Xy(670, 174),
                At("http"),
                At("cqrs"),
                At("handlers"),
                At("data"),
                At("ef"),
                At("sqliteef"),
                At("sqlite"),
                Xy(620, 656),
                At("appdb"),
                Xy(905, 500),
                At("http"),
                Xy(670, 174),
            ],
            [],
            LandsRow: true),

        // 4 -- what outlives the request. Starts at the commit, not at a click.
        new(
            "The work that outlives the request",
            "the outbox row committed with the order is relayed after the response, queueing mail and a delayed job",
            [
                At("appdb"),
                At("outbox"),
                At("cqrs"),
                At("handlers"),
                At("mail"),
                At("jobs"),
                At("appdb"),
                At("dashboard"),
                At("webpush"),
            ],
            [],
            LandsRow: false),
    ];

    // ---- static wiring that is not a band boundary ----
    private static IEnumerable<Component> Wires()
    {
        var parts = new List<Component>();

        // The build rail feeds the whole spine rather than any one band, so one bracket rather than ten
        // arrows: the generators produce the code every band below is made of.
        parts.Add(SvgPath
            .D($"M {Num(LeftRailX + RailW)} 300 H {Num(SpineX - 8)}")
            .Class("rf-hint-wire"));

        // The label sits UNDER the rail, not beside the connector: the 16px between the rail and the
        // spine is not enough for text, and putting it there ran it over the band heading.
        parts.Add(SvgText
            .X(Num(LeftRailX))
            .Y("590")
            .Class("rf-t rf-xxs rf-faint")["compiles into everything"]);
        parts.Add(SvgText
            .X(Num(LeftRailX))
            .Y("604")
            .Class("rf-t rf-xxs rf-faint")["on the right →"]);

        // The ops rail reads the tables; it is not on the request path, so the connectors are dashed.
        foreach (var (y, i) in new[] { 525.0, 617.0, 708.0 }.Select((v, i) => (v, i)))
        {
            parts.Add(SvgPath
                .Key($"ops{Id(i)}")
                .D($"M {Num(SpineX + SpineW)} {Num(y)} H {Num(RightRailX - 8)}")
                .Class("rf-hint-wire"));
        }

        return parts;
    }

    // ---- the packets ----
    //
    // A group per scene, moved by generated translate() keyframes. The circle sits at the origin and the
    // GROUP is translated, so the keyframes carry absolute coordinates straight from the waypoint table.
    private static IEnumerable<Component> Packets()
    {
        var parts = new List<Component>();

        for (var i = 0; i < SceneDefs.Length; i++)
        {
            parts.Add(Packet($"rf-pk{Id(i)}"));

            if (SceneDefs[i].Second.Length > 0)
            {
                parts.Add(Packet($"rf-pk{Id(i)}b"));
            }
        }

        return parts;
    }

    private static Component Packet(string cls) =>
        G.Key($"pk-{cls}").Class($"rf-pk {cls}")[
            Circle.Cx("0").Cy("0").R("11").Class("rf-pk-halo"),
            Circle.Cx("0").Cy("0").R("5").Class("rf-pk-core")
        ];
}
