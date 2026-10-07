namespace Rask;

/// <summary>
///     Flux's <c>flux:navlist</c>: a column of <see cref="UiNavlistItem" />s and <see cref="UiNavlistGroup" />s —
///     a sidebar's navigation, a settings page's sections.
/// </summary>
/// <remarks>
///     A <c>&lt;nav&gt;</c> landmark. A page with more than one needs each named, which is what
///     <see cref="AccessibleLabel" /> is for — "Main", "Settings".
/// </remarks>
public sealed partial class UiNavlist : Component
{
    /// <summary>The landmark's name, for a page with more than one navigation.</summary>
    public string? AccessibleLabel { get; set; }

    /// <summary>How the rows are drawn. <see cref="Ui.NavlistVariant.Outline" /> is Flux's sidebar look.</summary>
    public Ui.NavlistVariant? Variant { get; set; }

    /// <summary>Classes for the call site, added to the navlist's own — a width, most often.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var nav = Nav.Class("flex flex-col", Class);
        if (AccessibleLabel is { } label)
        {
            nav = nav.Aria("label", label);
        }

        // The variant is the navlist's and the look is each item's: an item reads it from here.
        return nav.Attributes(("data-ui-navlist", Variant == Ui.NavlistVariant.Outline ? "outline" : null))[Children ?? []];
    }
}
