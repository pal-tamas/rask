using Rask.Core.Routing;

// One namespace for the whole kit, with files grouping components rather than naming namespaces: a
// consuming page composes the kit from a single using, and a component can move between files without a
// churn diff at every call site.
namespace Rask.Ui;

/// <summary>
/// The surface's frame: a well, and the panel the surface is drawn on.
/// </summary>
/// <remarks>
/// Full-bleed on a phone and a bordered card from <c>sm</c> up. That is the whole responsive story for the
/// outer shell — on a 360px screen the margins and the rounded corner cost about 8% of the usable width and
/// buy nothing, so below <c>sm</c> the console simply IS the page.
/// </remarks>
public sealed partial class UiShell : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        // .rask-ops is a hook, not a fence. It was a fence while these pages rendered inside the host
        // application's document and every rule had to be scoped under it; the console is now a mounted
        // application with its own document (RaskMountedApp), so the stylesheet is free to be ordinary.
        Div.Class("rask-ops min-h-screen bg-ui-well text-ui-ink")[
            Div.Class("mx-auto w-full max-w-[110rem] sm:p-4 lg:p-6")[
                Div.Class("bg-ui-bg sm:overflow-hidden sm:rounded-2xl sm:border sm:border-ui-line")[
                    Children ?? []
                ]
            ]
        ];
}

/// <summary>The breadcrumb bar: what you are looking at, and how to get to a sibling of it.</summary>
public sealed partial class UiTopBar : Component
{
    /// <summary>Pushed to the trailing edge — links, never state an operator has to act on.</summary>
    public Component? Trailing { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Header.Class("flex items-center gap-1 border-b border-ui-line px-2 py-2 sm:gap-2 sm:px-4 sm:py-2.5")[
            Children ?? [],
            Trailing is null ? null : Div.Class("ml-auto flex items-center gap-1 pl-2 sm:gap-3")[Trailing]
        ];
}

/// <summary>The surface's mark and name. Goes home.</summary>
/// <remarks>
/// The destination and the wordmark are the caller's, not the kit's — this component used to name the
/// operator console's own overview route and print "Ops", which is exactly the coupling that kept the kit
/// inside one application.
/// </remarks>
public sealed partial class UiBrand : Component
{
    /// <summary>The wordmark. Hidden below <c>sm</c>, so it is never the only thing naming the page.</summary>
    public required string Label { get; set; }

    /// <summary>Where the mark goes. Home, for whatever this surface calls home.</summary>
    public required RouteUrl Href { get; set; }

    /// <summary>The mark itself. The overview glyph unless said otherwise.</summary>
    public UiIconName? Icon { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        NavLink
            .Href(Href)
            .Class(
                "flex min-h-11 shrink-0 items-center gap-2 rounded-lg px-1.5 text-sm font-semibold tracking-tight "
                + "text-ui-ink no-underline hover:bg-ui-well sm:min-h-0 sm:py-1.5")[
            UiIcon.Name(Icon ?? UiIconName.Overview).Class("size-5 shrink-0"),
            // The wordmark is the first thing to go: on a phone the crumb beside it says where you are,
            // which is the part someone actually needs.
            Span.Class("hidden sm:inline")[Label]
        ];
}

/// <summary>The rule between two crumbs.</summary>
public sealed partial class UiCrumbSeparator : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Span.Class("select-none text-sm text-ui-line").Attributes(("aria-hidden", "true"))["/"];
}

/// <summary>
/// The section tab bar: the underlined row that says which part of the console you are in.
/// </summary>
/// <remarks>
/// Scrolls sideways rather than wrapping. A wrapped tab bar changes the page's header height as the number
/// of registered batteries changes, which moves the content under an operator's thumb between one
/// deployment and the next; a scrolling one is always exactly one row tall.
/// </remarks>
public sealed partial class UiNav : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Nav.Class(
            "flex items-stretch gap-4 overflow-x-auto border-b border-ui-line px-3 sm:gap-6 sm:px-5 "
            + "[-ms-overflow-style:none] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden")[
            Children ?? []
        ];
}

/// <summary>One section tab.</summary>
public sealed partial class UiNavTab : Component
{
    public required string Label { get; set; }

    public required RouteUrl Href { get; set; }

    public bool? Active { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var active = Active == true;

        var tab = NavLink
            .Href(Href)
            // -mb-px pulls the tab's own bottom border onto the bar's, so the active underline replaces the
            // hairline rather than sitting above it.
            .Class(
                "-mb-px flex min-h-11 shrink-0 items-center whitespace-nowrap border-b-2 pb-2.5 pt-2.5 text-sm "
                + "no-underline " + (active
                    ? "border-ui-ink font-medium text-ui-ink"
                    : "border-transparent text-ui-muted hover:border-ui-line hover:text-ui-ink"));

        // Added only when it is true, rather than as one half of a ternary that has to yield a tuple either
        // way. The else branch of that shape ships a meaningless data-inactive on every inactive tab of
        // every page, and invites someone to start styling off it.
        if (active)
        {
            tab = tab.Attributes(("aria-current", "page"));
        }

        return tab[Label];
    }
}

/// <summary>
/// A breadcrumb level you can actually switch: a native select wearing the crumb's clothes.
/// </summary>
/// <remarks>
/// <para>
/// The reference opens a custom menu here. A menu is a popover, a popover is a key listener and an outside
/// click, and the console ships no JavaScript — so this is a real <c>&lt;select&gt;</c> with the chrome
/// stripped off it. That is not a consolation prize: it is keyboard-navigable for free, it announces itself
/// correctly, and on a phone it opens the platform's own picker, which is a better control than a menu
/// re-implemented in a div would have been.
/// </para>
/// <para>
/// The same trick the log's category filter already uses. Navigating on change rather than on a submit
/// means there is no button to press and nothing to forget to press.
/// </para>
/// </remarks>
public sealed partial class UiCrumbSwitcher : Component
{
    /// <summary>The accessible name. There is no visible label — the crumb's position is the label.</summary>
    public required string Label { get; set; }

    /// <summary>The option currently selected.</summary>
    public required string Value { get; set; }

    public required IReadOnlyList<(string Value, string Text)> Choices { get; set; }

    public Func<string, Task>? OnSelect { get; set; }

    public UiIconName? Icon { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var options = new List<Component?>();
        foreach (var (value, text) in Choices)
        {
            options.Add(Option
                .Key(value)
                .Value(value)
                .Selected(string.Equals(value, Value, StringComparison.Ordinal))[text]);
        }

        var select = Select
            .Value(Value)
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            // appearance-none strips the platform arrow so the crumb's own chevron can sit where the
            // reference puts it; the select underneath is otherwise completely ordinary.
            .Class(
                "min-h-11 w-full cursor-pointer appearance-none truncate rounded-lg border border-transparent "
                + "bg-transparent py-1.5 pr-7 text-sm text-ui-ink hover:bg-ui-well focus-visible:outline-2 "
                + "focus-visible:outline-offset-2 focus-visible:outline-ui-brand sm:min-h-0 "
                + (Icon is null ? "pl-2" : "pl-8"));

        if (OnSelect is { } select_)
        {
            select = select.OnChangeAsync(select_);
        }

        return Div.Class("relative flex min-w-0 max-w-[9rem] items-center sm:max-w-[16rem]")[
            Icon is { } icon
                ? UiIcon.Name(icon).Class("pointer-events-none absolute left-2 size-4 shrink-0 text-ui-muted")
                : null,
            select[options],
            UiIcon
                .Name(UiIconName.ChevronUpDown)
                .Class("pointer-events-none absolute right-2 size-4 shrink-0 text-ui-muted")
        ];
    }
}

/// <summary>A link out of the console, in the top bar's trailing edge.</summary>
public sealed partial class UiTopLink : Component
{
    public required string Label { get; set; }

    public required string Href { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        // A plain anchor, not a NavLink: this leaves the application, so it must be a browser navigation
        // rather than a live one. rel/target because it is a documentation site, not part of the console.
        A.Href(Href)
            .Target("_blank")
            .Rel("noopener noreferrer")
            .Class(
                "flex min-h-11 items-center rounded-lg px-2 text-sm text-ui-muted no-underline "
                + "hover:bg-ui-well hover:text-ui-ink sm:min-h-0 sm:py-1.5")[
            Label
        ];
}

/// <summary>The console's content column, inside the frame.</summary>
public sealed partial class UiMain : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Main.Class("bg-ui-well px-3 py-4 sm:px-5 sm:py-6")[Children ?? []];
}
