using Rask.Core.Routing;

// Rask.Dashboard.Pages, not .Ui: OpsIcon already sits in Pages/ under the root namespace, so this
// project's folders group files rather than name namespaces. Keeping the console's one namespace means a
// page composes this kit without a using, and a component can move between folders without a churn diff.
namespace Rask.Dashboard.Pages;

/// <summary>
/// The console's frame: a well, and the surface the console is drawn on.
/// </summary>
/// <remarks>
/// Full-bleed on a phone and a bordered card from <c>sm</c> up. That is the whole responsive story for the
/// outer shell — on a 360px screen the margins and the rounded corner cost about 8% of the usable width and
/// buy nothing, so below <c>sm</c> the console simply IS the page.
/// </remarks>
internal sealed partial class OpsShell : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        // .rask-ops is a hook, not a fence. It was a fence while these pages rendered inside the host
        // application's document and every rule had to be scoped under it; the console is now a mounted
        // application with its own document (RaskMountedApp), so the stylesheet is free to be ordinary.
        Div.Class("rask-ops min-h-screen bg-ops-well text-ops-ink")[
            Div.Class("mx-auto w-full max-w-[110rem] sm:p-4 lg:p-6")[
                Div.Class("bg-ops-bg sm:overflow-hidden sm:rounded-2xl sm:border sm:border-ops-line")[
                    Children ?? []
                ]
            ]
        ];
}

/// <summary>The breadcrumb bar: what you are looking at, and how to get to a sibling of it.</summary>
internal sealed partial class OpsTopBar : Component
{
    /// <summary>Pushed to the trailing edge — links, never state an operator has to act on.</summary>
    public Component? Trailing { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Header.Class("flex items-center gap-1 border-b border-ops-line px-2 py-2 sm:gap-2 sm:px-4 sm:py-2.5")[
            Children ?? [],
            Trailing is null ? null : Div.Class("ml-auto flex items-center gap-1 pl-2 sm:gap-3")[Trailing]
        ];
}

/// <summary>The console's mark and name. Goes home.</summary>
internal sealed partial class OpsBrand : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        NavLink
            .Href(Routes.OverviewPage())
            .Class(
                "flex min-h-11 shrink-0 items-center gap-2 rounded-lg px-1.5 text-sm font-semibold tracking-tight "
                + "text-ops-ink no-underline hover:bg-ops-well sm:min-h-0 sm:py-1.5")[
            OpsIcon.Name(OpsIconName.Overview).Class("size-5 shrink-0"),
            // The wordmark is the first thing to go: on a phone the crumb beside it says where you are,
            // which is the part an operator actually needs.
            Span.Class("hidden sm:inline")["Ops"]
        ];
}

/// <summary>The rule between two crumbs.</summary>
internal sealed partial class OpsCrumbSeparator : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Span.Class("select-none text-sm text-ops-line").Attributes(("aria-hidden", "true"))["/"];
}

/// <summary>
/// The section tab bar: the underlined row that says which part of the console you are in.
/// </summary>
/// <remarks>
/// Scrolls sideways rather than wrapping. A wrapped tab bar changes the page's header height as the number
/// of registered batteries changes, which moves the content under an operator's thumb between one
/// deployment and the next; a scrolling one is always exactly one row tall.
/// </remarks>
internal sealed partial class OpsNav : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Nav.Class(
            "flex items-stretch gap-4 overflow-x-auto border-b border-ops-line px-3 sm:gap-6 sm:px-5 "
            + "[-ms-overflow-style:none] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden")[
            Children ?? []
        ];
}

/// <summary>One section tab.</summary>
internal sealed partial class OpsNavTab : Component
{
    public required string Label { get; set; }

    public required RouteUrl Href { get; set; }

    public bool? Active { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var active = Active == true;

        return NavLink
            .Href(Href)
            // -mb-px pulls the tab's own bottom border onto the bar's, so the active underline replaces the
            // hairline rather than sitting above it.
            .Class(
                "-mb-px flex min-h-11 shrink-0 items-center whitespace-nowrap border-b-2 pb-2.5 pt-2.5 text-sm "
                + "no-underline " + (active
                    ? "border-ops-ink font-medium text-ops-ink"
                    : "border-transparent text-ops-muted hover:border-ops-line hover:text-ops-ink"))
            .Attributes(active ? ("aria-current", "page") : ("data-inactive", "true"))[
            Label
        ];
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
internal sealed partial class OpsCrumbSwitcher : Component
{
    /// <summary>The accessible name. There is no visible label — the crumb's position is the label.</summary>
    public required string Label { get; set; }

    /// <summary>The option currently selected.</summary>
    public required string Value { get; set; }

    public required IReadOnlyList<(string Value, string Text)> Choices { get; set; }

    public Func<string, Task>? OnSelect { get; set; }

    public OpsIconName? Icon { get; set; }

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
                + "bg-transparent py-1.5 pr-7 text-sm text-ops-ink hover:bg-ops-well focus-visible:outline-2 "
                + "focus-visible:outline-offset-2 focus-visible:outline-ops-brand sm:min-h-0 "
                + (Icon is null ? "pl-2" : "pl-8"));

        if (OnSelect is { } select_)
        {
            select = select.OnChangeAsync(select_);
        }

        return Div.Class("relative flex min-w-0 max-w-[9rem] items-center sm:max-w-[16rem]")[
            Icon is { } icon
                ? OpsIcon.Name(icon).Class("pointer-events-none absolute left-2 size-4 shrink-0 text-ops-muted")
                : null,
            select[options],
            OpsIcon
                .Name(OpsIconName.ChevronUpDown)
                .Class("pointer-events-none absolute right-2 size-4 shrink-0 text-ops-muted")
        ];
    }
}

/// <summary>A link out of the console, in the top bar's trailing edge.</summary>
internal sealed partial class OpsTopLink : Component
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
                "flex min-h-11 items-center rounded-lg px-2 text-sm text-ops-muted no-underline "
                + "hover:bg-ops-well hover:text-ops-ink sm:min-h-0 sm:py-1.5")[
            Label
        ];
}

/// <summary>The console's content column, inside the frame.</summary>
internal sealed partial class OpsMain : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Main.Class("bg-ops-well px-3 py-4 sm:px-5 sm:py-6")[Children ?? []];
}
