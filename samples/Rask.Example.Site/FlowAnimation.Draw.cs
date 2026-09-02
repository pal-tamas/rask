namespace Rask.Example.Site;

// Turning the node table into shapes. Nothing here decides anything -- every position comes from
// FlowAnimation.Nodes.cs -- so a layout change is a table edit, never a hunt through drawing code.
internal sealed partial class FlowAnimation
{
    private readonly record struct BandDef(double Y, double H, string Label);

    private static readonly BandDef[] BandDefs =
    [
        new(BandFrontEnd, HFrontEnd, "the front end — pick one, they share everything below"),
        new(BandTransport, HTransport, "transport"),
        new(BandRender, HRender, "render core (one walk, two outputs)   ·   the render ladder"),
        new(BandApp, HApp, "application logic"),
        new(BandBatteries, HBatteries, "durable batteries — all on the app's own database"),
        new(BandData, HData, "data access"),
        new(BandStorage, HStorage, "storage & durability"),
    ];

    private static IEnumerable<Component> Bands()
    {
        var parts = new List<Component>();

        for (var i = 0; i < BandDefs.Length; i++)
        {
            var band = BandDefs[i];
            parts.Add(G.Key($"band{Id(i)}")[
                Rect
                    .X(Num(SpineX))
                    .Y(Num(band.Y))
                    .Width(Num(SpineW))
                    .Height(Num(band.H))
                    .Rx("12")
                    .Class("rf-band"),
                SvgText.X(Num(SpineX + 4)).Y(Num(band.Y - 6)).Class("rf-t rf-xxs rf-mut")[band.Label]
            ]);
        }

        // The band-to-band connectors. Deliberately plain lines with no arrowheads: the same trunk
        // carries the request down and the rendered diff back up, so a fixed arrow would be wrong half
        // the time. Direction is what the moving packet is for.
        var gaps = new[]
        {
            (BandFrontEnd + HFrontEnd, BandTransport),
            (BandTransport + HTransport, BandRender),
            (BandRender + HRender, BandApp),
            (BandApp + HApp, BandBatteries),
            (BandBatteries + HBatteries, BandData),
            (BandData + HData, BandStorage),
        };

        var g = 0;
        foreach (var (from, to) in gaps)
        {
            foreach (var x in new double[] { 400, 620, 840 })
            {
                parts.Add(Line
                    .Key($"gap{Id(g++)}")
                    .X1(Num(x))
                    .Y1(Num(from))
                    .X2(Num(x))
                    .Y2(Num(to))
                    .Class("rf-trunk"));
            }
        }

        return parts;
    }

    // ---- boxes ----
    private static IEnumerable<Component> NodeShapes()
    {
        var parts = new List<Component>();

        foreach (var node in SpineNodes)
        {
            parts.Add(node.Kind == "screen" ? Screen(node) : Box(node));
        }

        parts.Add(ChipRow());
        parts.AddRange(RenderCoreArrows());
        parts.AddRange(DataArrows());
        return parts;
    }

    private static Component Box(Node node)
    {
        var children = new List<Component>
        {
            Rect
                .X(Num(node.X))
                .Y(Num(node.Y))
                .Width(Num(node.W))
                .Height(Num(node.H))
                .Rx("7")
                .Class($"rf-box rf-{node.Kind}"),
        };

        // One label centres in the box; a label plus a sub-label splits the height between them. Doing
        // it by height rather than by a fixed offset keeps the 20px chips and the 56px rail boxes
        // reading the same way.
        var cx = node.X + (node.W / 2);
        if (node.Sub is null)
        {
            children.Add(SvgText
                .X(Num(cx))
                .Y(Num(node.Y + (node.H / 2) + 4))
                .TextAnchor("middle")
                .Class("rf-t rf-xs")[node.Label]);
        }
        else
        {
            children.Add(SvgText
                .X(Num(cx))
                .Y(Num(node.Y + (node.H / 2) - 1))
                .TextAnchor("middle")
                .Class("rf-t rf-xs")[node.Label]);
            children.Add(SvgText
                .X(Num(cx))
                .Y(Num(node.Y + (node.H / 2) + 13))
                .TextAnchor("middle")
                .Class("rf-t rf-xxs rf-mut")[node.Sub]);
        }

        children.Add(Glow(node));
        return G.Key($"n-{node.Key}").Class($"rf-n rf-n-{node.Key}")[children];
    }

    // The highlight ring. Last in the group so it sits above the fill, and inset by half its stroke so
    // it reads as the box lighting up rather than as a second box around it.
    private static Component Glow(Node node) =>
        Rect
            .X(Num(node.X - 1))
            .Y(Num(node.Y - 1))
            .Width(Num(node.W + 2))
            .Height(Num(node.H + 2))
            .Rx("8")
            .Class($"rf-glow rf-glow-{node.Key}");

    // ---- the browser window: the only node whose CONTENT changes ----
    //
    // This is the payoff of the whole diagram. A reader can follow a packet down eight bands and back,
    // but the claim being made is "and then the page changes" -- so the page has to visibly change.
    private static Component Screen(Node node)
    {
        var children = new List<Component>
        {
            Rect.X(Num(node.X)).Y(Num(node.Y)).Width(Num(node.W)).Height(Num(node.H)).Rx("8").Class("rf-screen"),
            Rect.X(Num(node.X)).Y(Num(node.Y)).Width(Num(node.W)).Height("22").Rx("8").Class("rf-screenbar"),
            Rect.X(Num(node.X)).Y(Num(node.Y + 14)).Width(Num(node.W)).Height("8").Class("rf-screenbar"),
            Circle.Cx(Num(node.X + 14)).Cy(Num(node.Y + 11)).R("3").Class("rf-dot"),
            Circle.Cx(Num(node.X + 26)).Cy(Num(node.Y + 11)).R("3").Class("rf-dot"),
            Circle.Cx(Num(node.X + 38)).Cy(Num(node.Y + 11)).R("3").Class("rf-dot"),
            SvgText.X(Num(node.X + 52)).Y(Num(node.Y + 15)).Class("rf-t rf-xxs rf-mut")[node.Label],
            SvgText.X(Num(node.X + 10)).Y(Num(node.Y + 42)).Class("rf-t rf-sm")["Orders"],
        };

        // The button the journey starts from, on the header line rather than over the list -- the first
        // version put the click ring on top of a row, where it read as a bullet point rather than as
        // something you press.
        children.Add(Rect
            .X(Num(node.X + node.W - 76))
            .Y(Num(node.Y + 30))
            .Width("66")
            .Height("16")
            .Rx("4")
            .Class("rf-clickbtn"));
        children.Add(SvgText
            .X(Num(node.X + node.W - 43))
            .Y(Num(node.Y + 41))
            .TextAnchor("middle")
            .Class("rf-t rf-xxs")["+ New"]);
        // The press indicator traces the button rather than sitting in the middle of it: a circle here
        // covered the label, and reading "+ N w" is worse than having no indicator at all.
        children.Add(Rect
            .X(Num(node.X + node.W - 76))
            .Y(Num(node.Y + 30))
            .Width("66")
            .Height("16")
            .Rx("4")
            .Class("rf-click"));

        // Three settled rows, drawn as bars rather than fake data: invented names read as content and
        // pull the eye away from the row that actually matters.
        for (var i = 0; i < 3; i++)
        {
            children.Add(Rect
                .Key($"row{Id(i)}")
                .X(Num(node.X + 10))
                .Y(Num(node.Y + 54 + (i * 18)))
                .Width(Num(node.W - 20))
                .Height("12")
                .Rx("3")
                .Class("rf-row"));
        }

        // The row the journey produces. Its own class so the keyframes can land it exactly when the
        // returning diff reaches the browser, and no earlier.
        children.Add(G.Class("rf-newrow")[
            Rect
                .X(Num(node.X + 10))
                .Y(Num(node.Y + 108))
                .Width(Num(node.W - 20))
                .Height("14")
                .Rx("3")
                .Class("rf-row rf-row-new"),
            SvgText
                .X(Num(node.X + 16))
                .Y(Num(node.Y + 118))
                .Class("rf-t rf-xxs rf-signal")["+ new order"]
        ]);

        children.Add(Glow(node));
        return G.Key($"n-{node.Key}").Class($"rf-n rf-n-{node.Key}")[children];
    }

    // ---- the front-end chips ----
    private static Component ChipRow()
    {
        var parts = new List<Component>();

        // Islands, on their own row. Vue and Svelte are dashed because they are landing, and Blazor is
        // dashed AND marked, because it is a different KIND of island: Rask renders it to HTML itself,
        // so unlike the others its subtree is never opaque.
        for (var i = 0; i < IslandChips.Length; i++)
        {
            var chip = IslandChips[i];
            var x = 640 + (i * 66);
            parts.Add(G.Key($"isl{Id(i)}").Class($"rf-n rf-n-island-{chip.Label.ToLowerInvariant()}")[
                Rect.X(Num(x)).Y("124").Width("60").Height("28").Rx("7").Class($"rf-box rf-{chip.Kind}"),
                SvgText.X(Num(x + 30)).Y("142").TextAnchor("middle").Class("rf-t rf-xxs")[chip.Label]
            ]);
        }

        for (var i = 0; i < SpaTemplates.Length; i++)
        {
            var name = SpaTemplates[i];
            var x = 640 + (i * 56);
            parts.Add(G.Key($"spa{Id(i)}").Class($"rf-n rf-n-spa-{name.ToLowerInvariant()}")[
                Rect.X(Num(x)).Y("160").Width("50").Height("28").Rx("7").Class("rf-box rf-chip"),
                SvgText.X(Num(x + 25)).Y("178").TextAnchor("middle").Class("rf-t rf-xxs")[name]
            ]);
        }

        return G[parts];
    }

    // ---- within-band pipelines, which DO have a direction ----
    private static IEnumerable<Component> RenderCoreArrows() =>
        new[] { 296.0, 408.0, 520.0 }.Select((x, i) => Arrow($"rc{Id(i)}", x, 353, 12));

    private static IEnumerable<Component> DataArrows() =>
        new[] { 396.0, 526.0, 716.0 }.Select((x, i) => Arrow($"da{Id(i)}", x, 617, 10));

    // A polygon rather than a <marker>: a marker needs an id, and an id in an inline SVG is global to
    // the whole page, so two copies of this diagram would collide.
    private static Component Arrow(string key, double x, double y, double w) =>
        Polygon
            .Key($"ar-{key}")
            .Points($"{Num(x)},{Num(y - 4)} {Num(x + w)},{Num(y)} {Num(x)},{Num(y + 4)}")
            .Class("rf-arrow");

    // ---- rails ----
    // Captions are kept short on purpose: both rails are 152px wide, and anything longer runs out from
    // under the rail and collides with the spine (left) or the canvas edge (right).
    private static Component BuildRail() => Rail(
        BuildNodes,
        "before it runs",
        "generators & MSBuild",
        74);

    private static Component OpsRail() => Rail(
        OpsNodes,
        "operations",
        "reads what the bands write",
        398);

    private static Component Rail(Node[] nodes, string title, string caption, double labelY)
    {
        var parts = new List<Component>
        {
            SvgText.X(Num(nodes[0].X)).Y(Num(labelY)).Class("rf-t rf-xxs rf-mut")[title],
            SvgText.X(Num(nodes[0].X)).Y(Num(labelY + 12)).Class("rf-t rf-xxs rf-faint")[caption],
        };

        parts.AddRange(nodes.Select(Box));
        return G[parts];
    }

    // ---- legend + the caption that names the journey on screen ----
    private static Component Legend()
    {
        // The legend describes what is ADDED, not what is taken away — there is no "dimmed" state to
        // explain, because the map stays fully legible whichever journey is running.
        var parts = new List<Component>
        {
            Swatch("lit", LeftRailX, LegendY, "rf-sw-lit", "on this journey"),
            Swatch("soon", LeftRailX + 160, LegendY, "rf-sw-soon", "landing soon"),
            Swatch("blazor", LeftRailX + 310, LegendY, "rf-sw-blazor", "Rask-rendered island (not opaque)"),
            Swatch("off", LeftRailX + 550, LegendY, "rf-sw-off", "declared, not built"),
        };

        for (var i = 0; i < SceneDefs.Length; i++)
        {
            var scene = SceneDefs[i];
            parts.Add(G.Key($"cap{Id(i)}").Class($"rf-cap rf-cap{Id(i)}")[
                SvgText
                    .X(Num(CanvasW - LeftRailX))
                    .Y(Num(LegendY + 4))
                    .TextAnchor("end")
                    .Class("rf-t rf-sm")[scene.Title],
                SvgText
                    .X(Num(CanvasW - LeftRailX))
                    .Y(Num(LegendY + 24))
                    .TextAnchor("end")
                    .Class("rf-t rf-xs rf-mut")[scene.Caption]
            ]);
        }

        return G[parts];
    }

    private static Component Swatch(string key, double x, double y, string cls, string label) =>
        G.Key($"sw-{key}")[
            Rect.X(Num(x)).Y(Num(y - 9)).Width("12").Height("12").Rx("3").Class($"rf-box {cls}"),
            SvgText.X(Num(x + 20)).Y(Num(y + 1)).Class("rf-t rf-xxs rf-mut")[label]
        ];
}
