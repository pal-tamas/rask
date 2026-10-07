namespace Rask;

/// <summary>
///     Flux's <c>flux:breadcrumbs</c>: the trail from the top of the app to the page being shown, as
///     <see cref="UiBreadcrumbsItem" />s.
/// </summary>
/// <remarks>A plain <c>&lt;div&gt;</c>, as Flux's is.</remarks>
public sealed partial class UiBreadcrumbs : Component
{
    /// <summary>Classes for the call site, added to the trail's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("flex", Class).Attributes(("data-ui-breadcrumbs", null))[Children ?? []];
}
