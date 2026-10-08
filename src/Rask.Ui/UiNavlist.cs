namespace Rask;

/// <summary>
///     Flux's <c>flux:navlist</c>: a column of <see cref="UiNavlistItem" />s and <see cref="UiNavlistGroup" />s —
///     a sidebar's navigation, a settings page's sections.
/// </summary>
/// <remarks>A <c>&lt;nav&gt;</c> landmark, as Flux's is.</remarks>
public sealed partial class UiNavlist : Component
{
    /// <summary>How the rows are drawn. <see cref="Ui.NavlistVariant.Outline" /> is Flux's sidebar look.</summary>
    public Ui.NavlistVariant? Variant { get; set; }

    /// <summary>Classes for the call site, added to the navlist's own — a width, most often.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        // The variant is the navlist's and the look is each item's: an item reads it from here.
        Nav.Class("flex flex-col", Class)
            .Attributes(("data-ui-navlist", Variant == Ui.NavlistVariant.Outline ? "outline" : null))[Children ?? []];
}
