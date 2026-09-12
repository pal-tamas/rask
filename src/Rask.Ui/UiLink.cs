using Rask.Core.Routing;

namespace Rask.Ui;

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

    public UiTone? Tone { get; set; }

    /// <summary>Underlines on hover. Default true.</summary>
    public bool? Underline { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var classes = UiClass.Compose(
            "link",
            Underline == false ? "" : "link-hover",
            Tone is { } tone ? UiClassNames.LinkTone(tone) : "",
            Class);

        return Href.PageType is null
            ? A.Href(Href.ToString()).Class(classes)[Text]
            // No active class: a link in running text has no "you are here" state to show.
            : NavLink.Href(Href).ActiveClass("").Class(classes)[Text];
    }
}
