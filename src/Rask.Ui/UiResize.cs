namespace Rask.Ui;

/// <summary>Which way a box may be dragged bigger.</summary>
/// <remarks>
/// CSS's own <c>resize</c>, as a closed set. Flux UI's <c>resize</c> on a textarea, and the reason it is worth
/// a property: the handle is the browser's, and the only way to take it away or narrow it is a class — which,
/// written as a string at the call site, is a class the kit's stylesheet never compiled.
/// </remarks>
public enum UiResize
{
    /// <summary>Taller only. What a textarea does by default, and what suits a growing answer.</summary>
    Vertical,

    /// <summary>Wider only.</summary>
    Horizontal,

    /// <summary>Both.</summary>
    Both,

    /// <summary>Fixed. For a box in a layout the extra size would break — a table row, a grid cell.</summary>
    None,
}
