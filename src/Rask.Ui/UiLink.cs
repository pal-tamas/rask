using Rask.Core.Routing;

namespace Rask;

/// <summary>
/// A link, in the theme's colours.
/// </summary>
/// <remarks>
/// <para>
/// <c>link-hover</c> is the default rather than an option: a link that is only distinguishable by colour
/// fails for readers who cannot see the difference, and an underline on hover alone is not enough — the
/// surrounding text has to be what tells them. Set <see cref="Underline" /> to false only where something
/// else already marks it as a link.
/// </para>
/// <para>
/// Given a generated route it navigates inside the app: it renders as a <c>NavLink</c>, so the runtime
/// routes the click without a reload and the href carries the deploy's path base. A plain string is an
/// ordinary <c>&lt;a&gt;</c>, for a URL that leaves the app.
/// </para>
/// </remarks>
public sealed partial class UiLink : Component
{
    public new required string Text { get; set; }

    /// <summary>
    ///     Where it goes: a generated route (<c>Routes.Orders()</c>) to stay inside the app, or a URL string to
    ///     leave it.
    /// </summary>
    public required RouteUrl Href { get; set; }

    public Ui.Tone? Tone { get; set; }

    /// <summary>Underlines on hover. Default true.</summary>
    public bool? Underline { get; set; }

    /// <summary>
    ///     Marks a link that leaves the site: it opens in a new tab and says so.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Flux UI's <c>external</c>. Three things together, because any one of them alone is worse than none:
    ///     <c>target="_blank"</c> to open it away from the page, <c>rel="noopener noreferrer"</c> because a new
    ///     tab opened without it can reach back through <c>window.opener</c>, and a small mark with a
    ///     screen-reader-only "(opens in a new tab)" — a tab that opens unannounced takes the back button away
    ///     from a reader who did not ask for it.
    ///     </para>
    ///     <para>
    ///     For a <c>RouteUrl</c> that is a plain string. A generated route is one of your own pages and is not
    ///     external by definition, so this is ignored there rather than opening your own app in a second tab.
    ///     </para>
    /// </remarks>
    public bool? External { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var classes = UiClass.Compose(
            "link",
            Underline == false ? "" : "link-hover",
            Tone is { } tone ? UiClassNames.LinkTone(tone) : "",
            External == true && Href.PageType is null ? "inline-flex items-center gap-1" : "",
            Class);

        if (Href.PageType is not null)
        {
            // No active class: a link in running text has no "you are here" state to show.
            return NavLink.Href(Href).ActiveClass("").Class(classes)[Text];
        }

        var anchor = A.Href(Href.ToString()).Class(classes);
        if (External != true)
        {
            return anchor[Text];
        }

        return anchor.Target("_blank").Rel("noopener noreferrer")[
            Text,
            // Ui.Icon is aria-hidden throughout, so the mark is decoration and the words below carry the meaning.
            Ui.Icon.Name(Ui.IconName.ExternalLink).Class("size-3.5 shrink-0"),
            Span.Class("sr-only")[" (opens in a new tab)"]
        ];
    }
}
