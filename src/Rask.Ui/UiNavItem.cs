using Rask.Core.Routing;

namespace Rask.Ui;

/// <summary>
/// One place in a <see cref="UiNavList" />: a link with an icon, a label and, optionally, a badge.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Current" /> is worked out for you.</b> Unset, the item compares its <see cref="Href" /> — or
/// <see cref="Match" />, for an item that should stay current across a whole section — with the page being shown,
/// the way <c>NavLink</c> does, because it IS a <c>NavLink</c>: the current item carries daisyUI's
/// <c>menu-active</c> and <c>aria-current="page"</c>, and a generated route navigates inside the app. Set it to say so
/// yourself.
/// </para>
/// <para>
/// "Current", Flux UI's word and ARIA's, rather than "Active": the item is the page you are on, not a pressed control.
/// </para>
/// </remarks>
public sealed partial class UiNavItem : Component
{
    /// <summary>The words shown.</summary>
    public required string Label { get; set; }

    /// <summary>Where it goes. A generated route navigates inside the app; a string is an ordinary link.</summary>
    public required RouteUrl Href { get; set; }

    public UiIconName? Icon { get; set; }

    /// <summary>A count or a word at the end of the row — "12", "New".</summary>
    public string? Badge { get; set; }

    /// <summary>The badge's colour.</summary>
    public UiTone? BadgeTone { get; set; }

    /// <summary>Whether this is the page being shown. Unset, it is worked out from the route.</summary>
    public bool? Current { get; set; }

    /// <summary>The path compared with the page being shown, when it is not <see cref="Href" />.</summary>
    public RouteUrl? Match { get; set; }

    /// <summary>
    ///     Current for every page under <see cref="Match" /> (or <see cref="Href" />), not only that exact page — the
    ///     item for a section.
    /// </summary>
    public bool? MatchPrefix { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component[] content =
        [
            Icon is { } icon ? UiIcon.Name(icon).Class("size-4 shrink-0") : null!,
            Span.Class("grow")[Label],
            Badge is { } badge
                ? Span.Class(UiClass.Compose(
                    "badge badge-sm",
                    BadgeTone is { } tone ? UiClassNames.BadgeTone(tone) : "badge-ghost"))[badge]
                : null!
        ];

        Component link;
        if (Current is { } current)
        {
            // Stated: a plain anchor (or NavLink for in-app navigation) whose current state is what the call site said.
            var anchor = NavLink
                .Href(Href)
                .ActiveClass("")
                .Class(current ? UiClass.Compose("menu-active", Class) : Class);
            link = (current ? anchor.Aria("current", "page") : anchor)[content];
        }
        else
        {
            var anchor = NavLink
                .Href(Href)
                .ActiveClass("menu-active")
                .Class(Class);
            if (Match is { } match)
            {
                anchor = anchor.Match(match);
            }

            if (MatchPrefix == true)
            {
                anchor = anchor.ActiveMatch(NavLinkMatch.Prefix);
            }

            link = anchor[content];
        }

        return Li[link];
    }
}
