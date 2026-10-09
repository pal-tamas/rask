namespace Rask;

/// <summary>
///     Flux's <c>flux:navlist</c>: a column of <see cref="UiNavlistItem" />s and <see cref="UiNavlistGroup" />s —
///     a sidebar's navigation, a settings page's sections.
/// </summary>
/// <remarks>A <c>&lt;nav&gt;</c> landmark, as Flux's is.</remarks>
public sealed partial class UiNavlist : Component
{
    /// <summary>Classes for the call site, added to the navlist's own — a width, most often.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Nav.Class("flex flex-col", Class).Attributes(("data-ui-navlist", null))[Children ?? []];
}
