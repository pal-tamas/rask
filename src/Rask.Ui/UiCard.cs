using Rask.Core.Routing;

namespace Rask;

/// <summary>
/// A bordered panel with an optional heading, icon and action in its corner — and, given an
/// <see cref="Href" />, one link.
/// </summary>
/// <remarks>
/// It does not space what it holds. Every demo result on the site sits in one of these, and a card that
/// inserted a gap between its children would restyle all of them at once; a card that holds several sections
/// is usually several cards.
/// </remarks>
public sealed partial class UiCard : Component
{
    // Not `Title`: that name is the <title> tag's builder entry, inherited from Component.
    public string? Heading { get; set; }

    /// <summary>
    ///     The heading's level in the page's outline, 1 to 6. <c>&lt;h2&gt;</c> unless this says otherwise — a card
    ///     usually sits under the page's own heading, and one nested under a section heading is a level deeper.
    /// </summary>
    public int? HeadingLevel { get; set; }

    public Component? Action { get; set; }

    /// <summary>Shown before the heading.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>How tight the card's padding is — daisyUI's own card sizes.</summary>
    /// <remarks>
    ///     The class map for this already existed and nothing reached it: a card could not be made denser
    ///     without a raw class string, which is the one thing a call site drawn with the kit must not need.
    /// </remarks>
    public Ui.Size? Size { get; set; }

    /// <summary>Makes the whole card one link.</summary>
    /// <remarks>
    /// A link rather than a click handler, so it is reachable by keyboard, opens in a new tab and says where it
    /// goes — the same reasoning as <see cref="UiStat.Href" />. Everything inside becomes the link's content, so
    /// nothing inside may be interactive: an <see cref="Action" /> on a linked card should be a status, never a
    /// button.
    /// </remarks>
    public RouteUrl? Href { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component? heading = Heading is null ? null : global::Rask.UiHeading.Element(HeadingLevel ?? 2)(UiStyles.Heading)[Heading];
        Component? title = Icon is { } icon
            ? Div.Class("flex min-w-0 items-center gap-2")[
                Ui.Icon.Name(icon).Class("size-5 shrink-0 opacity-60"),
                heading
            ]
            : heading;

        Component? header = title is null && Action is null
            ? null
            // Wraps rather than truncating: a card's action is often a button whose label is the only thing
            // saying what it does, and on a phone the heading and the action rarely fit on one line.
            : Div.Class("mb-4 flex flex-wrap items-center justify-between gap-3")[title, Action];

        var classes = UiClass.Compose(
            UiStyles.Card,
            Size is { } size ? UiClassNames.CardSize(size) : "",
            Href is null ? "" : "block no-underline transition-colors hover:bg-base-200",
            Class);

        // A generated route navigates inside the app; a string is an ordinary link, as on Ui.Link and Ui.Button (#1070).
        return Href switch
        {
            null => Div.Class(classes)[header, Children ?? []],
            { PageType: null } url => A.Href(url.ToString()).Class(classes)[header, Children ?? []],
            { } route => NavLink.Href(route).Class(classes)[header, Children ?? []],
        };
    }
}
