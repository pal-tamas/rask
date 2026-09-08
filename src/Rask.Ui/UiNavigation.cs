namespace Rask.Ui;

/// <summary>
/// A link, in the theme's colours.
/// </summary>
/// <remarks>
/// <c>link-hover</c> is the default rather than an option: a link that is only distinguishable by colour
/// fails for readers who cannot see the difference, and an underline on hover alone is not enough — the
/// surrounding text has to be what tells them. Set <see cref="Underline" /> to false only where something
/// else already marks it as a link.
/// </remarks>
public sealed partial class UiLink : Component
{
    public new required string Text { get; set; }

    public required string Href { get; set; }

    public UiTone? Tone { get; set; }

    /// <summary>Underlines on hover. Default true.</summary>
    public bool? Underline { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        A.Href(Href).Class(UiClass.Compose(
            "link",
            Underline == false ? "" : "link-hover",
            Tone is { } tone ? UiClassNames.LinkTone(tone) : "",
            Class))[Text];
}

/// <summary>
/// The trail of where this page sits.
/// </summary>
/// <remarks>
/// The last crumb is the current page and is deliberately not a link — a link to where you already are is
/// a dead end that reads as navigation.
/// </remarks>
public sealed partial class UiBreadcrumbs : Component
{
    /// <summary>Each step: the words, and where it goes. A null href is the page you are on.</summary>
    public required IReadOnlyList<(string Text, string? Href)> Items { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Nav.Class(UiClass.Compose("breadcrumbs text-sm", Class))
            .Aria(new Dictionary<string, string?> { ["label"] = "Breadcrumb" })[
            Ul[
                // Keyed by the crumb's own text: a trail's identity is what it says, and keying by index
                // would let the diff reuse one crumb's element for another when a level is inserted.
                Items.Select(item => Li.Key(item.Text)[
                    item.Href is { } href ? A.Href(href)[item.Text] : Span[item.Text]
                ])
            ]
        ];
}

/// <summary>
/// A vertical list of links.
/// </summary>
/// <remarks>
/// A real <c>&lt;ul&gt;</c> of <c>&lt;li&gt;</c>: daisyUI's menu styles that shape, and it is also what
/// tells a screen reader how many items there are and which one it is on.
/// </remarks>
public sealed partial class UiMenu : Component
{
    public UiSize? Size { get; set; }

    /// <summary>Lays the items out in a row. daisyUI's <c>menu-horizontal</c>.</summary>
    public bool? Horizontal { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ul.Class(UiClass.Compose(
            "menu",
            Horizontal == true ? "menu-horizontal" : "",
            Size is { } size ? UiClassNames.MenuSize(size) : "",
            Class))[Children ?? []];
}

/// <summary>
/// One entry in a <see cref="UiMenu" />.
/// </summary>
public sealed partial class UiMenuItem : Component
{
    public new required string Text { get; set; }

    public string? Href { get; set; }

    public UiIconName? Icon { get; set; }

    /// <summary>The page this entry leads to is the page being shown.</summary>
    public bool? Active { get; set; }

    public Action? OnClick { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component inner = Href is { } href
            ? A.Href(href).Class(Active == true ? "menu-active" : "")[Content()]
            : Button.Type("button").Class(Active == true ? "menu-active" : "").OnClick(OnClick)[Content()];

        return Li.Class(UiClass.Compose(Class))[inner];
    }

    private Component Content() =>
        [
            Icon is { } icon ? UiIcon.Name(icon).Class("size-4 shrink-0") : null,
            Span[Text]
        ];
}

/// <summary>
/// The bar across the top of a page.
/// </summary>
/// <remarks>
/// Three slots rather than children, because that is the shape daisyUI's navbar lays out — leading,
/// centre, trailing — and a single children list would leave the caller writing the three wrappers by
/// hand every time.
/// </remarks>
public sealed partial class UiNavbar : Component
{
    public Component? Start { get; set; }

    public Component? Center { get; set; }

    public Component? End { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Nav.Class(UiClass.Compose("navbar bg-base-100", Class))[
            Start is null ? null : Div.Class("navbar-start")[Start],
            Center is null ? null : Div.Class("navbar-center")[Center],
            End is null ? null : Div.Class("navbar-end")[End]
        ];
}

/// <summary>
/// Progress through a sequence of named steps.
/// </summary>
public sealed partial class UiSteps : Component
{
    /// <summary>Stacks the steps vertically, which is what a phone has room for.</summary>
    public bool? Vertical { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ul.Class(UiClass.Compose(
            "steps",
            Vertical == true ? "steps-vertical" : "steps-horizontal",
            Class))[Children ?? []];
}

/// <summary>
/// One step in a <see cref="UiSteps" />.
/// </summary>
/// <remarks>
/// <see cref="Tone" /> is what marks a step as reached: daisyUI colours a step only when it carries one,
/// so the steps up to and including the current one take a tone and the rest take none.
/// </remarks>
public sealed partial class UiStep : Component
{
    public new required string Text { get; set; }

    public UiTone? Tone { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Li.Class(UiClass.Compose(
            "step",
            Tone is { } tone ? UiClassNames.StepTone(tone) : "",
            Class))[Text];
}

/// <summary>
/// A bar of destinations pinned to the bottom of the viewport, for a phone.
/// </summary>
/// <remarks>
/// The mobile counterpart to <see cref="UiNavbar" />: thumbs reach the bottom of a phone screen and not
/// the top. It is fixed to the viewport, so a page using one needs padding at its end or the last row
/// sits underneath it.
/// </remarks>
public sealed partial class UiDock : Component
{
    public UiSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(
            "dock",
            Size is { } size ? UiClassNames.DockSize(size) : "",
            Class))[Children ?? []];
}

/// <summary>
/// Numbered pages, as a joined row of buttons.
/// </summary>
/// <remarks>
/// daisyUI has no pagination component of its own — it is <c>join</c> plus buttons, which is what this
/// renders. The current page is a button that is <c>disabled</c> rather than merely styled: it is not an
/// action, and letting it be pressed re-navigates to where the reader already is.
/// </remarks>
public sealed partial class UiPagination : Component
{
    public required int Pages { get; set; }

    public required int Current { get; set; }

    public Action<int>? OnSelect { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("join", Class))
            .Aria(new Dictionary<string, string?> { ["label"] = "Pagination" })[
            Enumerable.Range(1, Math.Max(Pages, 0)).Select(page =>
            {
                var button = Button
                    .Key(page)
                    .Type("button")
                    .Class(UiClass.Compose("join-item btn", page == Current ? "btn-active" : ""))
                    .Disabled(page == Current);

                if (OnSelect is { } select && page != Current)
                {
                    button = button.OnClick(() => select(page));
                }

                return button[page.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            })
        ];
}

/// <summary>
/// A navigation bar whose entries open full panels beneath it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The browser opens this one, not C#</b> — the same story as <see cref="UiFab" />, for a different
/// mechanism. daisyUI builds the megamenu on the native popover API: each trigger is a
/// <c>popovertarget</c> and each panel a <c>[popover]</c>, and the open state is the browser's
/// <c>:popover-open</c>. There is no <c>megamenu-open</c> class to write, so nothing a page sets can
/// force it. <c>megamenu-active</c> is the sliding highlight, not the state.
/// </para>
/// <para>
/// That is a good trade here rather than a reluctant one. The browser supplies the top layer, Escape,
/// light-dismiss and focus containment, and the whole thing works on a prerendered page with no runtime
/// booted — which for a site's main navigation, the first thing a reader touches and the last thing
/// that should wait for a bundle, is worth more than programmatic control.
/// </para>
/// <para>
/// daisyUI positions the panels with CSS anchor positioning keyed on <c>:nth-of-type</c>, so the
/// triggers must be siblings of one another and the panels siblings of one another. Rendering them as
/// this component does is what keeps that true; wrapping either in a div of your own breaks the
/// numbering and the panels land in the wrong place.
/// </para>
/// </remarks>
public sealed partial class UiMegamenu : Component
{
    public UiSize? Size { get; set; }

    /// <summary>Stretches a panel to the full width of the viewport.</summary>
    public bool? Full { get; set; }

    /// <summary>Widens the panels without going edge to edge.</summary>
    public bool? Wide { get; set; }

    /// <summary>Stacks the triggers instead of laying them in a row, for a sidebar.</summary>
    public bool? Vertical { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Nav.Class(UiClass.Compose(
            "megamenu",
            Size is { } size ? UiClassNames.MegamenuSize(size) : "",
            Full == true ? "megamenu-full" : "",
            Wide == true ? "megamenu-wide" : "",
            Vertical == true ? "megamenu-vertical" : "",
            Class))[
            Children ?? [],
            // The moving highlight, and it goes LAST on purpose: daisyUI selects the panels as
            // `[popover]:nth-of-type(n)`, which counts divs, so a div placed among them would shift
            // every panel's number and send the highlight to the wrong trigger.
            Div.Class("megamenu-active")
        ];
}

/// <summary>
/// One trigger of a <see cref="UiMegamenu" /> and the panel it opens.
/// </summary>
/// <remarks>
/// <see cref="Id" /> joins the two halves through <c>popovertarget</c>, so it has to be unique on the
/// page — two panels sharing one would give the first two triggers and the second none.
/// </remarks>
public sealed partial class UiMegamenuPanel : Component
{
    /// <summary>The label on the trigger.</summary>
    public required string Trigger { get; set; }

    /// <summary>Unique on the page. It is what the trigger names.</summary>
    public required string Id { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        // Two roots and no wrapper between them: the trigger has to be a sibling of the other triggers
        // and the panel a sibling of the other panels, or daisyUI's nth-of-type anchoring misnumbers.
        [
            Button.Type("button").Attributes(("popovertarget", Id))[Trigger],
            // No class of its own. daisyUI styles the panel through `.megamenu [popover]`, so a name
            // invented here would style nothing while looking as though it did.
            Div.Id(Id).Class(Class).Popover("auto")[Children ?? []]
        ];
}
