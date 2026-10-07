using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     Flux's <c>flux:breadcrumbs.item</c>: one step of a <see cref="UiBreadcrumbs" /> trail, and the separator
///     after it.
/// </summary>
/// <remarks>
///     <para>
///     With an <see cref="Href" /> it is a link; without one it is plain, greyed text — the page being shown. Its words are its
///     children, an <see cref="Icon" /> can stand in for them, and anything else — a dropdown holding the steps
///     that were folded away — goes in as a child too.
///     </para>
///     <para>
///     The separator is a chevron that turns with the reading direction and is not drawn after the last item.
///     <see cref="Separator" /> names another icon — <see cref="Ui.IconName.Slash" /> is Flux's other one.
///     </para>
/// </remarks>
public sealed partial class UiBreadcrumbsItem : Component
{
    private const string SeparatorClass = "mx-1 text-zinc-300 group-last/breadcrumb:hidden dark:text-white/80";

    /// <summary>Where it goes. Without one the item is text, not a link.</summary>
    public RouteUrl? Href { get; set; }

    /// <summary>An icon before the words, or in place of them.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>Which drawing of the icon. Mini when unset.</summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <summary>The icon drawn after the item. A chevron when unset.</summary>
    public Ui.IconName? Separator { get; set; }

    /// <summary>Classes for the call site, added to the item's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component?[] content =
        [
            Icon is { } icon ? Ui.Icon.Name(icon).Variant(IconVariant ?? Ui.IconVariant.Mini) : null,
            .. Children ?? [],
        ];

        return Div.Class("group/breadcrumb flex items-center text-sm font-medium", Class)
            .Attributes(("data-ui-breadcrumbs-item", null))[
            Step(content),
            Separator is { } separator
                ? Ui.Icon.Name(separator).Mini.Class(SeparatorClass)
                : Ui.Icon.Name(Ui.IconName.ChevronRight).Mini.Class(SeparatorClass, "rtl:hidden"),
            Separator is null
                ? Ui.Icon.Name(Ui.IconName.ChevronLeft).Mini.Class(SeparatorClass, "hidden rtl:inline rtl:group-last/breadcrumb:hidden")
                : null
        ];
    }

    private Component Step(Component?[] content)
    {
        const string link =
            "text-zinc-800 underline-offset-4 decoration-zinc-800/20 hover:underline dark:text-white dark:decoration-white/20";
        if (Href is not { } href)
        {
            return Div.Class("text-gray-500 dark:text-white/80")[content];
        }

        return href.PageType is null
            ? A.Href(href.ToString()).Class(link)[content]
            : NavLink.Href(href).ActiveClass("").Class(link)[content];
    }
}
