using Rask.Core.Routing;

namespace Rask;

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

    /// <summary>The mark itself. The overview glyph unless said otherwise, or <see cref="Logo" /> instead.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>
    ///     A real logo, as the URL of an image, in place of <see cref="Icon" />.
    /// </summary>
    /// <remarks>
    ///     Flux UI's <c>logo</c>. A product's own mark is an SVG or a PNG it ships, never a glyph from someone
    ///     else's icon set, and until this existed the only way to show one was to drop out of the kit and write
    ///     the anchor by hand. It is decorative — <see cref="Label" /> is already the accessible name of the
    ///     link — so it carries an empty <c>alt</c> rather than repeating the name.
    /// </remarks>
    public string? Logo { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        const string classes =
            "flex min-h-11 shrink-0 items-center gap-2 rounded-lg px-1.5 text-sm font-semibold tracking-tight "
            + "text-base-content no-underline hover:bg-base-200 sm:min-h-0 sm:py-1.5";

        Component[] content =
        [
            Logo is { Length: > 0 } logo
                ? Img.Src(logo).Alt("").Class("size-5 shrink-0 object-contain")
                : Ui.Icon.Name(Icon ?? Ui.IconName.Overview).Class("size-5 shrink-0"),
            // The wordmark is the first thing to go: on a phone the crumb beside it says where you are,
            // which is the part someone actually needs. ui-rail-hide takes it for the same reason when a
            // collapsable sidebar is narrowed to its rail.
            Span.Class("ui-rail-hide hidden sm:inline")[Label]
        ];

        // A generated route navigates inside the app; a string is an ordinary link — the path base is not added to
        // it, so a brand pointing at "#" or at another site stays exactly that (#1070).
        // `title` for the rail and the phone, where the wordmark is hidden: see Ui.NavItem for why not a CSS tooltip.
        return Href.PageType is null
            ? A.Href(Href.ToString()).Class(classes).Title(Label)[content]
            : NavLink.Href(Href).Class(classes).Title(Label)[content];
    }
}
