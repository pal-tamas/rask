using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     Flux's <c>flux:brand</c>: the product's mark and name, linking home, at the head of a header or a sidebar.
/// </summary>
/// <remarks>
///     <see cref="Logo" /> is either an image's address or the mark itself: <c>.Logo("/logo.png")</c> draws the
///     image, <c>.Logo(Ui.Icon.Name(Ui.IconName.RocketLaunch).Micro)</c> draws what it is handed, and
///     <see cref="LogoClass" /> dresses the box around it — Flux's <c>logo</c> prop and its <c>logo</c> slot.
///     Leave <see cref="Name" /> out for the mark alone.
/// </remarks>
public sealed partial class UiBrand : Component
{
    /// <summary>The product's name, beside the mark.</summary>
    public string? Name { get; set; }

    /// <summary>The mark: a string is an image's address, anything else is drawn as it is.</summary>
    public Component? Logo { get; set; }

    /// <summary>The image's alternative text. Empty unless set: the name beside it already says it.</summary>
    public string? Alt { get; set; }

    /// <summary>Where it goes. Home (<c>/</c>) unless set.</summary>
    public RouteUrl? Href { get; set; }

    /// <summary>Classes for the box around a mark that is not an image — its fill, its ink, its shape.</summary>
    public string? LogoClass { get; set; }

    /// <summary>Classes for the call site, added to the brand's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var classes = UiClass.Compose(Name is null ? "me-4 flex h-10 items-center" : "me-4 flex h-10 items-center gap-2", Class);
        Component?[] content =
        [
            Mark(),
            Name is { } name
                ? Div.Class("truncate text-sm font-medium text-zinc-800 dark:text-zinc-100")[name]
                : null,
        ];

        var href = Href ?? (RouteUrl)"/";
        return href.PageType is null
            ? A.Href(href.ToString()).Class(classes).Attributes(("data-ui-brand", null))[content]
            : NavLink.Href(href).ActiveClass("").Class(classes).Attributes(("data-ui-brand", null))[content];
    }

    private Component? Mark() => Logo switch
    {
        null => null,
        global::Rask.Core.Components.Text { Value: var src } =>
            Div.Class("flex size-6 shrink-0 items-center justify-center overflow-hidden rounded-sm")[
                Img.Src(src).Alt(Alt ?? "").Class("h-6 max-w-full")
            ],
        var mark =>
            Div.Class("flex h-6 min-w-6 shrink-0 items-center justify-center overflow-hidden rounded-sm", LogoClass)[mark],
    };
}
