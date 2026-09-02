using System.Globalization;
using System.Text;

namespace Rask.Example.Site;

// The stylesheet, generated so the timing maths is written once and the packet routes come from the same
// waypoint table that positions the boxes.
//
// Colour works in two layers on purpose. Every rule paints with a local `--rf-*` alias, and each alias is
// defined as `var(--color-token, #literal)`. Inline in the page the site's @theme tokens are defined, so
// they win and the diagram follows the theme toggle. Baked into a standalone file none of them exist, so
// every alias falls through to its literal -- and because the aliases (not the fills) are what the
// prefers-color-scheme block redefines, the standalone file can answer the OS theme without any rule ever
// overriding the page's tokens. An <img>-loaded SVG cannot see the page's theme, which is exactly why
// that block has to exist.
internal sealed partial class FlowAnimation
{
    // One full cycle of all five journeys. Long, because a reader should be able to follow a packet
    // rather than track it.
    private const double Loop = 60;

    // Each journey owns a fifth of the loop; the packet travels for most of it and the result rests for
    // the remainder, which is what makes the arrival readable. A constant rather than Loop / scene count
    // because it is a keyframe percentage: the five slices must tile 100% exactly, with no rounding
    // drift between them. A test pins it to the number of journeys.
    private const double SceneSpan = 20;
    private const double TravelStart = 1;
    private const double TravelEnd = 16;

    // The pinned variant plays one journey on its own short loop.
    private const double PinLoop = 13;

    private static double SceneOrigin(int scene) => scene * SceneSpan;

    private static string Css(FlowScene? pinned)
    {
        var sb = new StringBuilder();

        AppendPalette(sb);
        AppendBase(sb);
        AppendNodeStates(sb);
        AppendPacketMotion(sb);
        AppendResultAndCaptions(sb, pinned);
        AppendReducedMotion(sb);

        return sb.ToString();
    }

    // ---- the two-layer palette ----
    private static void AppendPalette(StringBuilder sb) => sb.Append("""
        .rf-svg {
          --rf-ground: var(--color-ground, #14141b);
          --rf-panel: var(--color-panel, #1b1b24);
          --rf-panel2: var(--color-panel-2, #22222d);
          --rf-line: var(--color-line, rgba(139, 92, 246, 0.28));
          --rf-grid: var(--color-grid, rgba(139, 92, 246, 0.07));
          --rf-ink: var(--color-ink, #e9e9f2);
          --rf-inksoft: var(--color-ink-soft, #c3c2d6);
          --rf-muted: var(--color-muted, #8a8aa4);
          --rf-accent: var(--color-accent, #8b5cf6);
          --rf-accentink: var(--color-accent-ink, #c4b5fd);
          --rf-signal: var(--color-signal, #34d399);
        }

        /* Standalone only. Inline, every --color-* above is defined and wins, so redefining the ALIASES
           here can never override the page's own theme -- it only changes which literal is used when
           there is no theme to follow. */
        @media (prefers-color-scheme: light) {
          .rf-svg {
            --rf-ground: var(--color-ground, #f9f8fc);
            --rf-panel: var(--color-panel, #f2f1f7);
            --rf-panel2: var(--color-panel-2, #e7e6ef);
            --rf-line: var(--color-line, rgba(124, 58, 237, 0.18));
            --rf-grid: var(--color-grid, rgba(124, 58, 237, 0.06));
            --rf-ink: var(--color-ink, #2b2b35);
            --rf-inksoft: var(--color-ink-soft, #4b4a58);
            --rf-muted: var(--color-muted, #77768a);
            --rf-accent: var(--color-accent, #7c3aed);
            --rf-accentink: var(--color-accent-ink, #6d28d9);
            --rf-signal: var(--color-signal, #0f9d6e);
          }
        }


        """);

    private static void AppendBase(StringBuilder sb) => sb.Append("""
        .rf-svg { max-width: 100%; height: auto; }
        .rf-ground { fill: var(--rf-ground); }
        .rf-band { fill: var(--rf-grid); stroke: var(--rf-line); stroke-width: 1; }
        .rf-trunk { stroke: var(--rf-line); stroke-width: 2; }
        .rf-hint-wire { stroke: var(--rf-line); stroke-width: 1; stroke-dasharray: 3 4; fill: none; }
        .rf-arrow { fill: var(--rf-muted); }

        .rf-t {
          font-family: var(--sans, ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif);
          fill: var(--rf-ink);
        }
        .rf-lg { font-size: 19px; font-weight: 600; }
        .rf-sm { font-size: 14px; }
        .rf-xs { font-size: 12px; }
        .rf-xxs { font-size: 10.5px; }
        .rf-mut { fill: var(--rf-muted); }
        .rf-faint { fill: var(--rf-muted); opacity: 0.62; }
        .rf-signal { fill: var(--rf-signal); }

        .rf-box { stroke-width: 1; }
        .rf-core, .rf-app, .rf-data, .rf-wire { fill: var(--rf-panel2); stroke: var(--rf-line); }
        .rf-batt { fill: var(--rf-panel2); stroke: var(--rf-line); }
        .rf-batt-aside { fill: var(--rf-panel); stroke: var(--rf-line); stroke-dasharray: 4 3; }
        .rf-app-soft, .rf-data-soft, .rf-store-soft { fill: var(--rf-panel); stroke: var(--rf-line); }
        .rf-store { fill: var(--rf-panel2); stroke: var(--rf-accent); }
        .rf-rail, .rf-ops { fill: var(--rf-panel); stroke: var(--rf-line); }
        .rf-lane { fill: none; stroke: none; }
        .rf-chip, .rf-chip-wide { fill: var(--rf-panel2); stroke: var(--rf-line); }
        .rf-chip-note { fill: none; stroke: var(--rf-line); stroke-dasharray: 3 3; }
        /* Landing, not landed: dashed until the branch merges. */
        .rf-chip-soon { fill: none; stroke: var(--rf-accent); stroke-dasharray: 4 3; }
        /* A Blazor island is a different KIND -- Rask renders it, so its subtree is never opaque. */
        .rf-chip-blazor { fill: var(--rf-panel2); stroke: var(--rf-signal); stroke-width: 1.5; }
        /* Declared but not implemented: setting it throws at host build. */
        .rf-rung-off { fill: none; stroke: var(--rf-muted); stroke-dasharray: 2 4; }
        .rf-rung { fill: var(--rf-panel2); stroke: var(--rf-line); }

        .rf-screen { fill: var(--rf-panel); stroke: var(--rf-accent); }
        .rf-screenbar { fill: var(--rf-panel2); }
        .rf-dot { fill: var(--rf-muted); opacity: 0.5; }
        .rf-row { fill: var(--rf-muted); opacity: 0.22; }
        .rf-row-new { fill: var(--rf-signal); opacity: 0.3; }
        .rf-clickbtn { fill: var(--rf-panel2); stroke: var(--rf-accent); stroke-width: 1; }
        .rf-click { fill: none; stroke: var(--rf-accent); stroke-width: 1; opacity: 0; }

        .rf-sw-lit { fill: var(--rf-panel2); stroke: var(--rf-accent); }
        .rf-sw-blazor { fill: var(--rf-panel2); stroke: var(--rf-signal); stroke-width: 1.5; }
        .rf-sw-soon { fill: none; stroke: var(--rf-accent); stroke-dasharray: 3 2; }
        .rf-sw-off { fill: none; stroke: var(--rf-muted); stroke-dasharray: 2 3; }

        .rf-pk-core { fill: var(--rf-signal); }
        .rf-pk-halo { fill: var(--rf-signal); opacity: 0.22; }

        /* The highlight that marks the current path. Drawn as a ring around the box rather than a change
           to it, so nothing about the box's own legibility depends on whether it is currently lit. */
        .rf-glow {
          fill: none;
          stroke: var(--rf-accent);
          stroke-width: 2.5;
          opacity: 0;
          paint-order: stroke;
        }

        /* BASE STATE == THE FINAL FRAME: the whole map legible, the result landed, nothing in flight and
           nothing highlighted. prefers-reduced-motion turns every animation off and lands exactly here,
           which is a complete, readable architecture diagram. */
        .rf-n { opacity: 1; }
        .rf-pk { opacity: 0; }
        .rf-newrow { opacity: 1; }
        .rf-cap { opacity: 0; }
        .rf-cap0 { opacity: 1; }


        """);

    // ---- which boxes are on which journey ----
    //
    // The path is shown by ADDING a highlight, never by dimming everything else. The first version did
    // the opposite -- non-participants faded to 0.28 -- and it was wrong for a reason worth recording:
    // at any given instant most of a forty-box map is off the current path, so the diagram spent its
    // whole life mostly unreadable, and every still frame of it (the README's first paint, a screenshot,
    // a reduced-motion reader) showed the washed-out version. Emphasis is cheap; legibility is not.
    private static void AppendNodeStates(StringBuilder sb)
    {
        foreach (var node in AllNodes().Where(n => n.Scenes.Length > 0))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $".rf-auto .rf-glow-{node.Key} {{ animation: rf-lit-{node.Key} {Num(Loop)}s infinite; }}\n");
            sb.Append(CultureInfo.InvariantCulture, $"@keyframes rf-lit-{node.Key} {{ 0%");

            foreach (var scene in node.Scenes.OrderBy(s => s))
            {
                var start = SceneOrigin(scene);
                sb.Append(CultureInfo.InvariantCulture,
                    $", {Pct(start)}% {{ opacity: 0; }} {Pct(start + 1)}%, {Pct(start + SceneSpan - 1)}% {{ opacity: 1; }} {Pct(start + SceneSpan)}%");
            }

            sb.Append(", 100% { opacity: 0; } }\n");
        }

        sb.Append('\n');
    }

    // ---- the packets ----
    private static void AppendPacketMotion(StringBuilder sb)
    {
        for (var i = 0; i < SceneDefs.Length; i++)
        {
            var scene = SceneDefs[i];
            EmitPacket(sb, $"rf-pk{Id(i)}", i, scene.Path);

            if (scene.Second.Length > 0)
            {
                EmitPacket(sb, $"rf-pk{Id(i)}b", i, scene.Second);
            }
        }

        sb.Append('\n');
    }

    private static void EmitPacket(StringBuilder sb, string cls, int scene, Wp[] path)
    {
        // Auto: the packet is parked and invisible until its slice of the loop comes round.
        var origin = SceneOrigin(scene);
        sb.Append(CultureInfo.InvariantCulture,
            $".rf-auto .{cls} {{ animation: {cls}-a {Num(Loop)}s linear infinite; }}\n");
        sb.Append(CultureInfo.InvariantCulture, $"@keyframes {cls}-a {{\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"  0%, {Pct(origin + TravelStart - 0.4)}% {{ opacity: 0; transform: {Translate(path[0])}; }}\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"  {Pct(origin + TravelStart)}% {{ opacity: 1; transform: {Translate(path[0])}; }}\n");
        EmitWaypoints(sb, path, origin + TravelStart, TravelEnd - TravelStart);
        sb.Append(CultureInfo.InvariantCulture,
            $"  {Pct(origin + TravelEnd + 0.8)}%, 100% {{ opacity: 0; transform: {Translate(path[^1])}; }}\n}}\n");

        // Pinned: the same route, stretched over its own loop, with no dead time.
        sb.Append(CultureInfo.InvariantCulture,
            $".rf-only{Id(scene)} .{cls} {{ animation: {cls}-p {Num(PinLoop)}s linear infinite; }}\n");
        sb.Append(CultureInfo.InvariantCulture, $"@keyframes {cls}-p {{\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"  0% {{ opacity: 0; transform: {Translate(path[0])}; }}\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"  2% {{ opacity: 1; transform: {Translate(path[0])}; }}\n");
        EmitWaypoints(sb, path, 2, 86);
        sb.Append(CultureInfo.InvariantCulture,
            $"  92%, 100% {{ opacity: 0; transform: {Translate(path[^1])}; }}\n}}\n");
    }

    // Waypoints are spaced evenly. Uneven spacing would let a long hop and a short hop take the same
    // time, which reads as the packet speeding up for no reason.
    private static void EmitWaypoints(StringBuilder sb, Wp[] path, double from, double span)
    {
        for (var j = 1; j < path.Length; j++)
        {
            var at = from + (span * j / (path.Length - 1));
            var last = j == path.Length - 1;
            sb.Append(CultureInfo.InvariantCulture,
                $"  {Pct(at)}% {{ {(last ? "opacity: 1; " : string.Empty)}transform: {Translate(path[j])}; }}\n");
        }
    }

    private static string Translate(Wp p) =>
        $"translate({Num(p.X)}px, {Num(p.Y)}px)";

    // ---- the result landing, and the caption naming the journey ----
    private static void AppendResultAndCaptions(StringBuilder sb, FlowScene? pinned)
    {
        // The new row appears as each landing journey completes, and clears before the next begins.
        sb.Append(CultureInfo.InvariantCulture,
            $".rf-auto .rf-newrow {{ animation: rf-newrow {Num(Loop)}s infinite; }}\n@keyframes rf-newrow {{\n  0%");

        for (var i = 0; i < SceneDefs.Length; i++)
        {
            if (!SceneDefs[i].LandsRow)
            {
                continue;
            }

            var origin = SceneOrigin(i);
            sb.Append(CultureInfo.InvariantCulture,
                $", {Pct(origin + TravelEnd - 1)}% {{ opacity: 0; }} {Pct(origin + TravelEnd)}%, {Pct(origin + SceneSpan - 0.5)}% {{ opacity: 1; }} {Pct(origin + SceneSpan)}%");
        }

        sb.Append("""
            , 100% { opacity: 0; }
            }

            """);

        // The click ring pulses as each front-end journey starts.
        sb.Append(CultureInfo.InvariantCulture,
            $".rf-auto .rf-click {{ animation: rf-click {Num(Loop)}s infinite; }}\n");
        sb.Append("@keyframes rf-click { 0%");

        for (var i = 0; i < SceneDefs.Length; i++)
        {
            if (!SceneDefs[i].LandsRow)
            {
                continue;
            }

            // Opacity and stroke-width only. The earlier version animated `r`, which does nothing at all
            // once the indicator stopped being a circle.
            var origin = SceneOrigin(i);
            sb.Append(CultureInfo.InvariantCulture,
                $", {Pct(origin)}% {{ opacity: 0; stroke-width: 1; }} {Pct(origin + 1.2)}% {{ opacity: 1; stroke-width: 3; }} {Pct(origin + 3)}%");
        }

        sb.Append(", 100% { opacity: 0; stroke-width: 1; } }\n\n");

        // Captions: exactly one visible at a time in both variants.
        //
        // The first journey's caption is written to be visible AT 0%, not faded in from it. Otherwise
        // the one frame everybody sees most — the first paint, and every screenshot of the baked file —
        // is the frame with no caption at all.
        for (var i = 0; i < SceneDefs.Length; i++)
        {
            var origin = SceneOrigin(i);
            sb.Append(CultureInfo.InvariantCulture,
                $".rf-auto .rf-cap{Id(i)} {{ animation: rf-cap{Id(i)} {Num(Loop)}s infinite; }}\n");

            // Assigned before appending: a ternary collapses the interpolated-string HANDLER to a plain
            // string, and the culture-aware Append overload then no longer applies. Pct and Id are
            // already invariant, so the plain overload is correct here.
            var frames = i == 0
                ? $"@keyframes rf-cap0 {{ 0%, {Pct(SceneSpan - 0.5)}% {{ opacity: 1; }} {Pct(SceneSpan)}%, 100% {{ opacity: 0; }} }}\n"
                : $"@keyframes rf-cap{Id(i)} {{ 0%, {Pct(origin - 0.5)}% {{ opacity: 0; }} {Pct(origin)}%, {Pct(origin + SceneSpan - 0.5)}% {{ opacity: 1; }} {Pct(origin + SceneSpan)}%, 100% {{ opacity: 0; }} }}\n";

            sb.Append(frames);
        }

        sb.Append('\n');

        // Pinned: no cycling at all. Dim everything off the chosen path, light what is on it, and show
        // that journey's caption. Written last so it beats the equal-specificity rules above.
        if (pinned is null)
        {
            return;
        }

        // Pinned holds the chosen journey's highlight on permanently. Same principle as the cycling
        // variant: light the path, never dim the map.
        var only = (int)pinned.Value;
        sb.Append(".rf-pin .rf-cap { opacity: 0; }\n");

        foreach (var node in AllNodes().Where(n => n.Scenes.Contains(only)))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $".rf-only{Id(only)} .rf-glow-{node.Key} {{ opacity: 1; }}\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $".rf-only{Id(only)} .rf-cap{Id(only)} {{ opacity: 1; }}\n");
        sb.Append(CultureInfo.InvariantCulture,
            $".rf-only{Id(only)} .rf-newrow {{ opacity: {(SceneDefs[only].LandsRow ? "1" : "0")}; }}\n\n");
    }

    private static void AppendReducedMotion(StringBuilder sb) => sb.Append("""
        @media (prefers-reduced-motion: reduce) {
          .rf-glow, .rf-pk, .rf-newrow, .rf-cap, .rf-click { animation: none; }
          .rf-pk { opacity: 0; }
        }
        """);
}
