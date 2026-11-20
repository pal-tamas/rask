using System.Collections;

namespace Rask;

/// <summary>
///     A page of a paging <c>.Choices(…)</c>, as options, and how many rows its source still has past it when the source
///     said so (<see cref="Ui.ChoiceRows{T}" />): what the select draws its scrollbar by.
/// </summary>
/// <param name="rows">The page's rows, as the options drawn.</param>
/// <param name="left">How many rows of the source come after this page; null when the source did not say.</param>
internal sealed class UiPageAnswer(Component?[] rows, int? left) : IEnumerable<Component?>
{
    /// <summary>How many rows of the source come after this page; null when the source did not say.</summary>
    internal int? Left { get; } = left;

    /// <summary>
    ///     The answer for the page <paramref name="answered" /> the source gave after its first <paramref name="skip" /> rows.
    /// </summary>
    /// <param name="rows">The page's rows, as the options drawn (leading rows included, which the source never counts).</param>
    /// <param name="answered">What the source answered.</param>
    /// <param name="skip">How many of the source's rows came before the page.</param>
    /// <typeparam name="T">The type of a row's value.</typeparam>
    internal static UiPageAnswer Of<T>(Component?[] rows, IReadOnlyList<Ui.Choice<T>> answered, int skip) =>
        new(rows, answered is Ui.ChoiceRows<T> counted ? Math.Max(0, counted.Total - skip - counted.Count) : null);

    /// <inheritdoc />
    public IEnumerator<Component?> GetEnumerator() => ((IEnumerable<Component?>)rows).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
