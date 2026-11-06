using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rask.Flux;

/// <summary>
///     Holds one Rask example to its Flux twin: every marked node — <c>data-flux-*</c> there, <c>data-ui-*</c> here — is
///     paired with the node of the same marker, in document order, and its whole subtree is compared: tag, box, computed
///     styles, pseudo-elements, animations (timing and keyframes), and what hover / active / focus-visible change.
/// </summary>
/// <param name="slug">The page's slug: its own parts are held node for node, a neighbour from another page may be a stand-in.</param>
/// <param name="layout">A layout page (layouts/header, layouts/sidebar), whose parts are placed by the layout.</param>
/// <param name="own">The markers of the parts this page documents (<see cref="OwnParts" />).</param>
public sealed partial class ParityComparer(string slug, bool layout, IReadOnlySet<string> own)
{
    // Another component placed in a layout is compared as a box: where it is and how big. Its inside is its own
    // page's business, where it is compared whole.
    [GeneratedRegex("^data-flux-(navbar|navlist|navmenu|brand|profile|avatar|menu|heading|text|separator)")]
    private static partial Regex ForeignMarker();

    // In a layout the kit's controls are labels for the checkbox that holds the sidebar's state.
    private static readonly Dictionary<string, string[]> LayoutNative = new(StringComparer.Ordinal)
    {
        ["ui-sidebar-toggle"] = ["label", "div"],
        ["ui-dropdown"] = ["label"],
        ["button"] = ["label"],
    };

    // Not compared: what the surrounding docs page decides rather than the component (where a top-level
    // node sits, how wide a stretched one is), and the two the box already states.
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal) { "width", "height" };

    /// <summary>
    ///     Flux's own custom elements, which its script upgrades, and the native element Rask.Ui writes in their place
    ///     because it has that behaviour built in: a &lt;label for&gt; focuses its control with no script at all.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Native = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ui-field"] = "div",
        ["ui-label"] = "label",
        ["ui-description"] = "div",
        ["ui-legend"] = "legend",
        ["ui-progress"] = "div",
        ["ui-table-scroll-area"] = "div",
        ["ui-disclosure-group"] = "div",
        ["ui-disclosure"] = "details",
        ["ui-chart"] = "div",
        // Flux's pressable that is not a <button> (a kanban card): focusable, pressed with Enter and Space.
        ["ui-button"] = "button",
        // The tooltip's wrapper: the kit wires the trigger at render and the browser shows the [popover].
        // A toggleable tooltip is a <ui-dropdown> on Flux's page, under the tooltip's marker.
        ["ui-tooltip"] = "div",
        ["ui-dropdown"] = "div",
        // The menu family: a [popover] opens from `popovertarget` alone, and its rows are buttons.
        ["ui-context"] = "div",
        ["ui-menu"] = "div",
        ["ui-submenu"] = "div",
        ["ui-menu-radio-group"] = "div",
        ["ui-menu-checkbox-group"] = "div",
        ["ui-menu-radio"] = "button",
        ["ui-menu-checkbox"] = "button",
        // The select's, the autocomplete's and the pillbox's elements: a native popover and C# key handling in their place.
        ["ui-select"] = "div",
        ["ui-selected"] = "div",
        ["ui-options"] = "div",
        ["ui-option"] = "div",
        ["ui-option-empty"] = "div",
        ["ui-option-create"] = "div",
        ["ui-empty"] = "div",
        ["ui-pillbox"] = "div",
        ["ui-pillbox-trigger"] = "div",
        ["ui-selected-remove"] = "div",
        // The sidebar: a checkbox and CSS collapse it and slide it over the page, where Flux's element runs script.
        ["ui-sidebar"] = "div",
        // The checkbox, radio and switch: a <label> around the real <input>, and a group that is only a box.
        ["ui-checkbox-group"] = "div",
        ["ui-radio-group"] = "div",
        ["ui-checkbox"] = "label",
        ["ui-radio"] = "label",
        ["ui-switch"] = "label",
        // The modal's wrapper, and the one around a button that closes it: the kit's buttons are invoker commands.
        ["ui-modal"] = "div",
        ["ui-close"] = "div",
        // The editor and its toolbar: the kit writes the roles at render (toolbar, combobox, listbox, option) and
        // its engine does what Flux's elements script. `ui-menu` is the stand-in of the one example that puts a
        // dropdown menu in a toolbar.
        ["ui-editor"] = "div",
        ["ui-editor-content"] = "div",
        ["ui-toolbar"] = "div",
        // A <label> opens the file input inside it when clicked, where <ui-file-upload> does it with script.
        ["ui-file-upload"] = "label",
        // The calendar: its month steps and its today shortcut are real buttons.
        ["ui-calendar"] = "div",
        ["ui-calendar-months"] = "div",
        ["ui-calendar-month"] = "div",
        ["ui-calendar-year"] = "div",
        ["ui-calendar-previous"] = "button",
        ["ui-calendar-next"] = "button",
        ["ui-calendar-today"] = "button",
        // The date picker: the box Flux scripts its confirming button through (as `ui-close` is around the
        // cancelling one) is the box it leaves behind. Its presets are a `ui-radio-group` of RADIO_BUTTONs, below.
        ["ui-date-picker"] = "div",
        ["ui-date-picker-trigger"] = "div",
        ["ui-selected-date"] = "div",
        ["ui-calendar-presets"] = "div",
        ["ui-date-picker-select"] = "div",
        // The time picker: the list of times is a popover the browser opens.
        ["ui-time-picker"] = "div",
        ["ui-time-picker-trigger"] = "div",
        ["ui-selected-time"] = "div",
        ["ui-time-picker-options"] = "div",
        // The datetime picker: the date picker's popup with the time picker's fields in it.
        ["ui-datetime-picker"] = "div",
        ["ui-selected-datetime"] = "div",
        // The color picker: a modal <dialog> the runtime shows, and the box Flux scripts its Select button through.
        ["ui-color-picker"] = "div",
        ["ui-color-picker-select"] = "div",
        // The slider and the one-time code: a box around native inputs.
        ["ui-slider"] = "div",
        ["ui-otp"] = "div",
        // The tabs: a role="tablist" of buttons, with the runtime's arrow keys (rask-tabs.ts).
        ["ui-tab-group"] = "div",
        ["ui-tabs"] = "div",
        ["ui-tabs-scroll-area"] = "div",
        // The carousel: plain boxes around a native scroller, which the runtime's hook reports on (rask-carousel.ts).
        ["ui-carousel"] = "div",
        ["ui-carousel-slide"] = "div",
        ["ui-carousel-button"] = "div",
        ["ui-carousel-indicators"] = "div",
        // The phone field: a box around a native tel input and the select's elements, which the phone engine drives.
        ["ui-phone"] = "div",
        // The composer: a role="group" box; its submit keys and its growing textarea are the runtime's (rask-submit.ts, rask-grow.ts).
        ["ui-composer"] = "div",
    };

    // Flux's ui-checkbox, ui-radio and ui-switch ARE the control, by script. The <label> written in their place
    // holds the native <input> that is: one child Flux has no node for, and nothing drawn.
    private static readonly HashSet<string> HoldsInput = new(StringComparer.Ordinal) { "ui-checkbox", "ui-radio", "ui-switch" };

    // …and a Flux part, by its marker, that needs script to do what a native element does alone: a <label>
    // opens the file input inside it when clicked, where Flux's <div> calls input.click().
    // A toggle is a <ui-switch> too, by another marker: a real <button role="switch">, which Space and Enter press.
    private static readonly Dictionary<string, string> NativePart = new(StringComparer.Ordinal) { ["input-file"] = "label", ["toggle"] = "button" };

    // …and a part whose native control IS the component on the Rask side: Flux hides an <input type="range"> in
    // each slider thumb for the keyboard and moves the thumb by script; Rask.Ui lays that input, invisible,
    // over the track the thumb travels, and the browser drags it. Tag and place in the tree are compared; its
    // box and look are the platform's.
    private static readonly Dictionary<string, string> NativeControl = new(StringComparer.Ordinal) { ["slider-thumb"] = "input" };

    // Flux marks an accordion's root `data-flux-accordion-heading`, the marker its headings carry too.
    // …and leaves a chart's root with no marker at all: its <ui-chart> is the chart.
    private static readonly Dictionary<string, string> Mismarked = new(StringComparer.Ordinal) { ["ui-disclosure-group"] = "accordion", ["ui-chart"] = "chart" };

    // What the kit adds to do WITHOUT script what Flux does with it: a chart's hover strips, each carrying the
    // cursor and tooltip Flux's script would move there. Flux has no such node, so there is nothing to pair it with.
    // …and the box in a color picker's dialog that only the stylesheet sizes, which is how the page learns
    // whether the dialog is Flux's sheet: Flux asks the window. And the one in a calendar, which is how the page
    // learns that it is drawn for a phone.
    private static readonly string[] Extra = ["data-ui-chart-hover", "data-ui-color-picker-sheet", "data-ui-calendar-narrow"];

    // A popup is placed by script in Flux (`position: absolute` and an inset it computes) and by CSS anchor
    // positioning here (a popover in the top layer is `position: fixed`, the gap a margin). For exactly that pair,
    // how each says where the popup goes is not compared; where it ends up is — its offset from the root it
    // floats beside, like every other node's.
    private static readonly string[] Placement = ["position", "marginTop", "marginRight", "marginBottom", "marginLeft"];

    // A group whose parts are the browser's while they are typed into (docs/js-interop-runtime.md, rask-bound.ts)
    // holds ONE <input type="hidden"> that the page binds. Flux's custom element carries its value itself and
    // has no such node, so that field is no child here.
    private static readonly string[] Bound = ["data-rask-otp"];

    private static readonly string[] SvgParents = ["svg", "g", "defs", "clippath", "mask", "symbol"];

    /// <summary>
    ///     The markers of the parts <paramref name="slug" />'s page documents (flux:button.group -> button-group,
    ///     flux:icon.* -> icon), read from the committed catalogue. A part no Reference heads (<c>examples</c> in place of
    ///     <c>props</c>: flux:spacer, written on many pages) is no page's own.
    /// </summary>
    public static IReadOnlySet<string> OwnParts(string slug)
    {
        var snapshot = JsonNode.Parse(File.ReadAllText(Repo.Path("tests", "Rask.Ui.Tests", "Flux", "flux.snapshot.json")))!;
        var page = (snapshot["pages"] as JsonArray ?? []).FirstOrDefault(p => Js.Str(p!["slug"]) == slug);
        var parts = (page?["parts"] as JsonArray ?? [])
            .Where(part => part!["examples"] is null)
            .Select(part => Js.Str(part!["name"])!)
            .Select(name => name.StartsWith("flux:", StringComparison.Ordinal) ? name[5..] : name)
            .Select(name => name.EndsWith(".*", StringComparison.Ordinal) ? name[..^2] : name)
            .Select(name => name.Replace('.', '-'));
        return new HashSet<string>([slug, .. parts], StringComparer.Ordinal);
    }

    /// <summary>Every difference between Flux's example and the kit's; <paramref name="notes" /> collects the stand-ins let go.</summary>
    public List<string> CompareExample(Example theirs, Example mine, List<string> notes)
    {
        var a = Tops(theirs, "data-flux-");
        var b = Tops(mine, "data-ui-");
        var diffs = new List<string>();

        // Paired by marker, in document order. A part this page documents has to be there node for node. A
        // NEIGHBOUR from another page (the buttons beside a separator) with no marked node on the Rask side is
        // an unmarked stand-in: not compared, and said so.
        var names = new List<string>();
        foreach (var name in a.Select(n => Mark(n, "data-flux-")).Concat(b.Select(n => Mark(n, "data-ui-"))))
        {
            if (!names.Contains(name))
            {
                names.Add(name);
            }
        }

        foreach (var name in names)
        {
            var x = a.Where(n => Mark(n, "data-flux-") == name).ToList();
            var y = b.Where(n => Mark(n, "data-ui-") == name).ToList();
            if (!own.Contains(name) && !name.StartsWith($"{slug}-", StringComparison.Ordinal) && y.Count == 0)
            {
                notes.Add($"{name} ×{x.Count}: a stand-in on the Rask page, not compared");
                continue;
            }

            if (x.Count != y.Count)
            {
                diffs.Add($"marked nodes: Flux has {x.Count} {name}, Rask has {y.Count}");
                continue;
            }

            // A layout's parts are placed by the layout: where each is on the page is the thing being compared.
            if (layout)
            {
                for (var i = 0; i < x.Count; i++)
                {
                    if (Math.Abs(x[i].Box[0] - y[i].Box[0]) > 0.6 || Math.Abs(x[i].Box[1] - y[i].Box[1]) > 0.6)
                    {
                        diffs.Add($"{name}[{i}]: at {Compare.Fix(x[i].Box[0])},{Compare.Fix(x[i].Box[1])} vs {Compare.Fix(y[i].Box[0])},{Compare.Fix(y[i].Box[1])}");
                    }
                }
            }

            for (var i = 0; i < x.Count; i++)
            {
                var label = $"{name}[{i}]{(x[i].Text.Length > 0 ? $" \"{Slice(x[i].Text, 16)}\"" : "")}";
                CompareTree(theirs, x[i], mine, y[i], x[i], y[i], label, diffs);
            }
        }

        return diffs;
    }

    /// <summary>The part's marker is the shortest: Flux writes <c>data-flux-card</c> beside <c>data-flux-card-body-variant</c>.</summary>
    public static string Mark(MeasuredNode node, string prefix)
    {
        if (Mismarked.TryGetValue(node.Tag, out var mismarked))
        {
            return mismarked;
        }

        return node.Attrs.Select(attr => attr.Key).Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => k[prefix.Length..]).OrderBy(k => k.Length).FirstOrDefault() ?? node.Tag;
    }

    // Marked nodes with no marked ancestor: the components an example places.
    private static List<MeasuredNode> Tops(Example example, string prefix)
    {
        bool Marked(MeasuredNode n) => Mismarked.ContainsKey(n.Tag) || n.Attrs.Any(attr => attr.Key.StartsWith(prefix, StringComparison.Ordinal));
        return example.Nodes.Where(n => Marked(n) && !example.WithAncestors(n).Skip(1).Any(Marked)).ToList();
    }

    private bool Foreign(MeasuredNode node) => layout && node.Attrs.Any(attr => ForeignMarker().IsMatch(attr.Key));

    private bool SameTag(MeasuredNode a, MeasuredNode b)
    {
        var expected = NativePart.TryGetValue(Mark(a, "data-flux-"), out var part) ? part : Native.TryGetValue(a.Tag, out var native) ? native : a.Tag;
        // The <button> Flux scripts to open a <ui-disclosure> is a <details>' own <summary>. A <ui-radio> that is a
        // choice among buttons, not a form's radio (a date picker's preset), is a real <button role="radio">, which the
        // runtime's roving group walks.
        var radioButton = a.Tag == "ui-radio" && b.Tag == "button" && b.Attr("role") == "radio";
        return expected == b.Tag || (a.Tag == "button" && b.Tag == "summary") || radioButton
            || (layout && LayoutNative.TryGetValue(a.Tag, out var tags) && tags.Contains(b.Tag));
    }

    // The same holds for a modal <dialog> the kit anchors to its trigger (the color picker's), and for a popup the
    // runtime has placed (`data-rask-placed`, rask-place.ts): absolute at the inset it computes, its margins zeroed
    // where Flux's dialog keeps the `auto` they resolve from.
    private static bool Placed(MeasuredNode a, MeasuredNode b) => (b.Has("popover") || b.Tag == "dialog") && a.Style("position") == "absolute"
        && (b.Style("position") == "fixed" || (b.Style("position") == "absolute" && b.Has("data-rask-placed")));

    // A dialog the VIEWPORT places (`position: fixed` on both sides: Flux's date picker under 640 px, centred over
    // the page) is nowhere against the trigger that opened it — that distance is how far each page happened to be
    // scrolled. It is held to its place in the viewport, and what is inside it to the dialog.
    private static bool Centred(MeasuredNode a, MeasuredNode b) => a.Tag == "dialog" && a.Style("position") == "fixed" && b.Style("position") == "fixed";

    private static bool BoundField(MeasuredNode parent, MeasuredNode c) => c.Tag == "input" && c.Attr("type") == "hidden" && Bound.Any(parent.Has);

    // `data-parity-skip` on a Rask node says what of it is NOT this page's to match, and nothing more is let go:
    //   (no value)  a stand-in for a component not rebuilt yet: held to its place and size, inside not compared;
    //   "self"      its own look is another component's, its children are compared as usual;
    //   "width" / "height"   that dimension is random on Flux's page (`rand()` in the docs), here and below it.
    private void CompareTree(Example theirs, MeasuredNode a, Example mine, MeasuredNode b, MeasuredNode rootA, MeasuredNode rootB, string where, List<string> diffs, string free = "")
    {
        var skip = b.Attr("data-parity-skip");
        if (IsNativeControl(theirs, a) && a.Tag == b.Tag)
        {
            return;
        }

        var standIn = skip == "" || Foreign(a);
        var ownLook = !standIn && skip != "self";
        if (skip is "width" or "height")
        {
            free = skip;
        }

        if (!SameTag(a, b) && ownLook)
        {
            diffs.Add($"{where}: tag <{a.Tag}> vs <{b.Tag}>");
        }

        if (a.Text != b.Text && !ReferenceEquals(a, rootA) && ownLook)
        {
            diffs.Add($"{where}: text \"{a.Text}\" vs \"{b.Text}\"");
        }

        static bool Differs(double x, double y) => Math.Abs(x - y) > 0.6;
        bool[] held = [free != "width", free != "height"];
        if ((held[0] && Differs(a.Box[2], b.Box[2])) || (held[1] && Differs(a.Box[3], b.Box[3])))
        {
            diffs.Add($"{where}: size {Js.Num(a.Box[2])}x{Js.Num(a.Box[3])} vs {Js.Num(b.Box[2])}x{Js.Num(b.Box[3])}");
        }

        // A node that is not displayed — itself or by an ancestor — or that is 0×0 on both sides has no box:
        // its rectangle is the viewport's corner, which says how far each page is scrolled and nothing about it.
        var boxed = (theirs.Shown(a) || mine.Shown(b)) && new[] { a, b }.Any(n => n.Box[2] > 0 || n.Box[3] > 0);
        // Nor has a `display: contents` root (a modal's trigger): nothing under it can be placed against it.
        var anchored = rootA.Style("display") != "contents" || rootB.Style("display") != "contents";
        var floats = !ReferenceEquals(a, rootA) && boxed && Centred(a, b);
        if (floats)
        {
            static double At(Example example, MeasuredNode n, int i) => n.Box[i] + example.Origin[i];
            if (new[] { 0, 1 }.Any(i => Differs(At(theirs, a, i), At(mine, b, i))))
            {
                diffs.Add($"{where}: in the viewport at {Compare.Fix(At(theirs, a, 0))},{Compare.Fix(At(theirs, a, 1))} vs {Compare.Fix(At(mine, b, 0))},{Compare.Fix(At(mine, b, 1))}");
            }

            (rootA, rootB) = (a, b);
        }
        else if (!ReferenceEquals(a, rootA) && boxed && anchored)
        {
            static double Off(MeasuredNode n, MeasuredNode r, int i) => n.Box[i] - r.Box[i];
            var (ra, rb) = (rootA, rootB);
            if (new[] { 0, 1 }.Any(i => held[i] && Differs(Off(a, ra, i), Off(b, rb, i))))
            {
                diffs.Add($"{where}: offset {Compare.Fix(Off(a, ra, 0))},{Compare.Fix(Off(a, ra, 1))} vs {Compare.Fix(Off(b, rb, 0))},{Compare.Fix(Off(b, rb, 1))}");
            }
        }

        if (standIn)
        {
            return;
        }

        if (ownLook)
        {
            CompareLook(theirs, a, mine, b, where, diffs);
        }

        // A <template> is never drawn — Flux keeps a prototype of every chart node in one, and inside an <svg> a
        // template's children are ordinary DOM children — so it is no child on either side. Nor is an EXTRA node.
        // Nor is a <path> outside an <svg>: Flux's editor leaves one beside the link panel's check icon, and no browser draws it.
        // Nor is a <script>: Flux's phone carries its list of countries in one, data its script reads, never drawn.
        static bool Stray(MeasuredNode c, MeasuredNode n) => c.Tag is "template" or "script" || (c.Tag == "path" && !SvgParents.Contains(n.Tag));
        List<MeasuredNode> Kids(Example example, MeasuredNode n) => example.ChildrenOf(n).Where(c => !Stray(c, n) && !Extra.Any(c.Has)).ToList();
        var ca = Kids(theirs, a);
        var cb = Kids(mine, b).Where(c => !(HoldsInput.Contains(a.Tag) && c.Tag == "input") && !BoundField(b, c)).ToList();
        if (ca.Count != cb.Count)
        {
            diffs.Add($"{where}: children <{string.Join(' ', ca.Select(c => c.Tag))}> vs <{string.Join(' ', cb.Select(c => c.Tag))}>");
            return;
        }

        for (var i = 0; i < ca.Count; i++)
        {
            CompareTree(theirs, ca[i], mine, cb[i], rootA, rootB, $"{where} > {ca[i].Tag}[{i}]", diffs, free);
        }
    }

    // What one node looks like: computed styles, pseudo-elements, animations, and what each forced state changes.
    private void CompareLook(Example theirs, MeasuredNode a, Example mine, MeasuredNode b, string where, List<string> diffs)
    {
        var departs = Departed.From(mine, b);
        if (Pressables.SameArrow(a, b))
        {
            departs.Add("cursor");
        }

        var unheld = Placed(a, b) ? new HashSet<string>([.. Placement, .. departs]) : departs.Count > 0 ? new HashSet<string>(departs) : null;
        CompareStyles(a.Styles, b.Styles, where, diffs, unheld, b.Pressable);
        foreach (var pseudo in new[] { "::before", "::after" })
        {
            var (pa, pb) = (a.Pseudo(pseudo), b.Pseudo(pseudo));
            if ((pa is null) != (pb is null))
            {
                diffs.Add($"{where}{pseudo}: {(pa is not null ? "only in Flux" : "only in Rask")}");
            }

            // A pseudo-element inherits what its node departs in (a control's `user-select`).
            else if (pa is not null)
            {
                CompareStyles(pa, pb!, $"{where}{pseudo}", diffs, departs.Count > 0 ? new HashSet<string>(departs) : null, b.Pressable);
            }
        }

        // Flux's keyframes are named `flux-*` as its markers are; Rask's are `ui-*`.
        static string Moves(MeasuredNode n) => Js.Stringify(n.Animations ?? new JsonArray()).Replace("\"name\":\"flux-", "\"name\":\"ui-", StringComparison.Ordinal);
        // An open modal's focus placeholder: Flux's script takes it away once the dialog has focus, the kit's stylesheet
        // by an animation (ui-modal-placeholder) — the same accepted pair parity-modal names. Each ends with it gone.
        var placeholder = b.Has("data-ui-focus-placeholder");
        if (!placeholder && Moves(a) != Moves(b))
        {
            diffs.Add($"{where}: animations: {Moves(a)} vs {Moves(b)}");
        }

        // A long example is measured for its first 60 controls only, and the two pages need not run out at the
        // same node: a state is compared where both sides measured it.
        if (!theirs.Measured(a, layout) || !mine.Measured(b, layout))
        {
            return;
        }

        // A node that is not displayed on either side cannot be hovered, pressed or focused, so a state forced
        // onto it says nothing — and inside a closed menu something false: Flux lights a row from script
        // (`data-active`) where the kit has `:hover`. parity-menu holds an OPEN menu's rows under a real pointer.
        if (!theirs.Shown(a) && !mine.Shown(b))
        {
            return;
        }

        foreach (var state in new[] { "hover", "active", "focus-visible" })
        {
            var x = theirs.Changed(a, state);
            var y = mine.Changed(b, state);
            // A focused field is ringed in the accent (Departed.FocusFrom); parity-focus holds that ring.
            var ringed = state == "focus-visible" ? Departed.FocusFrom(b) : [];
            foreach (var key in x.Select(p => p.Key).Concat(y.Select(p => p.Key)).Distinct(StringComparer.Ordinal))
            {
                string? Xs(string k) => x.ContainsKey(k) ? Js.Str(x[k]) : a.Style(k);
                string? Ys(string k) => y.ContainsKey(k) ? Js.Str(y[k]) : b.Style(k);
                if (Compare.Undrawn(key, Xs, Ys) || ringed.Contains(key))
                {
                    continue;
                }

                var (xv, yv) = (Js.Str(x[key]), Js.Str(y[key]));
                if (!Compare.Same(xv, yv))
                {
                    diffs.Add($"{where}:{state} {key}: {xv ?? "(unchanged)"} vs {yv ?? "(unchanged)"}");
                }
            }
        }
    }

    private static bool IsNativeControl(Example example, MeasuredNode node) =>
        example.ParentOf(node) is { } parent && NativeControl.TryGetValue(Mark(parent, "data-flux-"), out var tag) && tag == node.Tag;

    // THE CHART'S TICKS, and nothing else: a `transform` on the chart's page is the same within 0.5 px (owner,
    // 2026-11-04, #1260). Flux lays its axes out around each tick label's getBBox(), which Chromium on Linux widens by the
    // glyph's overhang where a Mac gives the advance — the same Inter, the same 34.078 px "Nov 4" on both — so on the
    // runner Flux's ticks sit up to 0.24 px from where the kit's (and Flux's on a Mac) do. parity-chart says the same.
    private static double[]? Matrix(string? value)
    {
        var m = MatrixValue().Match(value ?? "");
        return m.Success ? m.Groups[1].Value.Split(',').Select(v => Js.Parse(v.Trim())).ToArray() : null;
    }

    private static bool NearTransform(string? x, string? y)
    {
        var (a, b) = (Matrix(x), Matrix(y));
        return a is not null && b is not null && a.Length == b.Length && a.Select((v, i) => Math.Abs(v - b[i]) <= (i >= 4 ? 0.5 : 1e-6)).All(ok => ok);
    }

    [GeneratedRegex(@"^matrix\(([^)]*)\)$")]
    private static partial Regex MatrixValue();

    /// <summary>
    ///     Every computed style of Flux's node <paramref name="x" /> the kit's <paramref name="y" /> does not match, but
    ///     those <paramref name="unheld" />. <paramref name="pressable" />: the kit's node shows the hand by the
    ///     owner-approved departure (<see cref="Pressables.CursorDeparted" />).
    /// </summary>
    public void CompareStyles(JsonObject x, JsonObject y, string where, List<string> diffs, ISet<string>? unheld = null, string pressable = "false")
    {
        foreach (var key in Pressables.Styles)
        {
            // A Flux measurement kept from before Styles named a key says nothing about it: `--refresh` measures it.
            if (!x.ContainsKey(key))
            {
                continue;
            }

            var (xv, yv) = (Js.Str(x[key]), Js.Str(y[key]));
            if (Ignored.Contains(key) || (unheld?.Contains(key) ?? false) || Compare.Undrawn(key, k => Js.Str(x[k]), k => Js.Str(y[k])) || Compare.Same(xv, yv))
            {
                continue;
            }

            if (slug == "chart" && key == "transform" && NearTransform(xv, yv))
            {
                continue;
            }

            if (key == "cursor" && Pressables.CursorDeparted(xv, yv, pressable))
            {
                continue;
            }

            diffs.Add($"{where}: {key}: {xv ?? "undefined"} vs {yv ?? "undefined"}");
        }
    }

    /// <summary><c>text.slice(0, n)</c>.</summary>
    public static string Slice(string text, int n) => text.Length > n ? text[..n] : text;
}
