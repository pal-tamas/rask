namespace Rask;

/// <summary>
///     Flux's <c>flux:breadcrumbs</c>: the trail from the top of the app to the page being shown, as
///     <see cref="UiBreadcrumbsItem" />s.
/// </summary>
/// <remarks>
///     Flux writes a plain <c>&lt;div&gt;</c>; this is that element with the landmark said on it —
///     <c>role="navigation"</c>, named "Breadcrumb" unless <see cref="AccessibleLabel" /> names it otherwise —
///     so a screen reader finds the trail as it does any other navigation.
/// </remarks>
public sealed partial class UiBreadcrumbs : Component
{
    /// <summary>The landmark's name. "Breadcrumb" when unset.</summary>
    public string? AccessibleLabel { get; set; }

    /// <summary>Classes for the call site, added to the trail's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("flex", Class)
            .Role("navigation")
            .Aria("label", AccessibleLabel ?? "Breadcrumb")
            .Attributes(("data-ui-breadcrumbs", null))[Children ?? []];
}
