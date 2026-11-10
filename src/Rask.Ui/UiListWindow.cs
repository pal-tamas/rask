namespace Rask;

/// <summary>
///     When a drawn list of options is windowed, and the measures its window is drawn by (<c>Ui.Select</c>'s listbox and
///     combobox, <c>Ui.Pillbox</c>).
/// </summary>
internal static class UiListWindow
{
    /// <summary>
    ///     A list of more options than this draws only the rows its scroll shows. Every example on Flux's pages has fewer,
    ///     and is drawn whole, as Flux draws it; a list of the rows of a table (1098 vehicles, 873 drivers) has more.
    /// </summary>
    internal const int Above = 100;

    /// <summary>A row's height in pixels: <see cref="UiListboxLook.Option" />'s padding and one line of words.</summary>
    internal const int Row = 32;

    /// <summary>A row with a line under its words (<see cref="UiListboxLook.OptionTall" />): two lines and their gap.</summary>
    internal const int TallRow = 60;

    /// <summary>The list's height before the browser has said it: the popup's twenty rem.</summary>
    internal const int Height = 320;

    /// <summary>How many rows past either edge of the box are drawn as well, so a scroll or a key finds them there.</summary>
    internal const int Overscan = 10;

    /// <summary>The most letters type-ahead looks for: more is no reader typing.</summary>
    internal const int MaxTyped = 64;
}
