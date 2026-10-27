namespace Rask;

/// <summary>One filter of a <see cref="UiFilters" />, as its badge says it.</summary>
/// <param name="Label">What it filters by: the control's label.</param>
/// <param name="Shown">What it holds, as the control shows it. Nothing for a box that is ticked or not.</param>
/// <param name="Active">Whether it is in force.</param>
/// <param name="Key">The address key it is kept under; none for a <see cref="UiFilter" />, which says how it is removed.</param>
internal sealed record UiFilterEntry(string Label, string? Shown, bool Active, string? Key)
{
    /// <summary>What a <see cref="UiFilter" /> runs when its badge is taken off.</summary>
    internal Callback OnRemove { get; init; }

    /// <summary>What tells its badge apart where it has no address key: a <see cref="UiFilter" />'s own key.</summary>
    internal object? Identity { get; init; }

    /// <summary>The badge's words: "Driver: Ada", or the label alone.</summary>
    internal string Words => string.IsNullOrEmpty(Shown) ? Label : Label + ": " + Shown;
}
