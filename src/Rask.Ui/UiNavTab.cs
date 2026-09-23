using Rask.Core.Routing;

namespace Rask;

/// <summary>One section tab in a <see cref="UiNav" /> bar.</summary>
/// <remarks>
/// <para>
/// <b><see cref="Active" /> is worked out for you</b>, the way <see cref="UiNavItem" />'s <c>Current</c> is: unset,
/// the tab compares its <see cref="Href" /> — or <see cref="Match" />, for a tab that should stay current across a
/// whole section — with the page being shown, because it IS a <c>NavLink</c>. Set it to say so yourself.
/// </para>
/// <para>
/// It used to be settable only. That is the same control as the sidebar's item, in a row rather than a column, and
/// having one derive its current state while the other silently showed none was a footgun rather than a choice:
/// nothing failed, the bar simply had no tab marked.
/// </para>
/// </remarks>
public sealed partial class UiNavTab : Component
{
    public required string Label { get; set; }

    public required RouteUrl Href { get; set; }

    /// <summary>Whether this is the page being shown. Unset, it is worked out from the route.</summary>
    public bool? Active { get; set; }

    /// <summary>An icon before the label.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>A count or a word after the label — "12", "New".</summary>
    public string? Badge { get; set; }

    /// <summary>The badge's colour.</summary>
    public Ui.Tone? BadgeTone { get; set; }

    /// <summary>The path compared with the page being shown, when it is not <see cref="Href" />.</summary>
    public RouteUrl? Match { get; set; }

    /// <summary>
    ///     Active for every page under <see cref="Match" /> (or <see cref="Href" />), not only that exact page — the
    ///     tab for a section.
    /// </summary>
    public bool? MatchPrefix { get; set; }

    public string? Class { get; set; }

    // -mb-px pulls the tab's own bottom border onto the bar's, so the active underline replaces the
    // hairline rather than sitting above it.
    private const string Base =
        "-mb-px flex min-h-11 shrink-0 items-center gap-2 whitespace-nowrap border-b-2 pb-2.5 pt-2.5 text-sm "
        + "no-underline";

    private const string On = "border-ui-ink font-medium text-base-content";

    private const string Off = "border-transparent opacity-60 hover:border-base-300 hover:text-base-content";

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component[] content =
        [
            Icon is { } icon ? Ui.Icon.Name(icon).Class("size-4 shrink-0") : null!,
            Span[Label],
            Badge is { } badge
                ? Span.Class(UiClass.Compose(
                    "badge badge-sm",
                    BadgeTone is { } tone ? UiClassNames.BadgeTone(tone) : "badge-ghost"))[badge]
                : null!
        ];

        // A generated route navigates inside the app; a STRING is an ordinary link, written exactly as given
        // with no path base added (#1070) — so it stays a plain anchor, and there is no route for it to be
        // compared against either. Its current state can only be stated.
        if (Href.PageType is null)
        {
            var plain = A.Href(Href.ToString()).Class(UiClass.Compose(Base, Active == true ? On : Off, Class));

            // Added only when it is true, rather than as one half of a ternary that has to yield a tuple either
            // way. The else branch of that shape ships a meaningless data-inactive on every inactive tab of
            // every page, and invites someone to start styling off it.
            return (Active == true ? plain.Attributes(("aria-current", "page")) : plain)[content];
        }

        // Stated: the call site owns it, so no route is consulted.
        if (Active is { } active)
        {
            var stated = NavLink
                .Href(Href)
                .ActiveClass("")
                .Class(UiClass.Compose(Base, active ? On : Off, Class));
            return (active ? stated.Aria("current", "page") : stated)[content];
        }

        // Worked out: NavLink compares the route and writes both the active class and aria-current.
        var link = NavLink
            .Href(Href)
            .ActiveClass(On)
            .Class(UiClass.Compose(Base, Off, Class));

        if (Match is { } match)
        {
            link = link.Match(match);
        }

        if (MatchPrefix == true)
        {
            link = link.ActiveMatch(NavLinkMatch.Prefix);
        }

        return link[content];
    }
}
