namespace Rask;

/// <summary>What a <see cref="UiSlider{T}" /> tells the ticks inside it: its scale, where its thumbs are, and how to move one.</summary>
/// <param name="Scale">The slider's ends and step.</param>
/// <param name="Values">One value, or the two of a range.</param>
/// <param name="Inside">Whether the ticks are drawn on the track.</param>
/// <param name="Pick">Moves the nearer thumb to a value; null while the slider is disabled.</param>
internal sealed record UiSliderScope(UiSliderScale Scale, decimal[] Values, bool Inside, Func<decimal, Task>? Pick)
{
    /// <summary>A tick the fill has reached: up to the thumb, or between the two of a range.</summary>
    internal bool Reaches(decimal value) =>
        Values.Length == 2 ? value >= Values[0] && value <= Values[1] : value <= Values[0];

    /// <summary>A tick a thumb is on.</summary>
    internal bool IsOn(decimal value) => Array.IndexOf(Values, value) >= 0;
}
