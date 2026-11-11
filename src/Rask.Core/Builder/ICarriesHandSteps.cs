namespace Rask.Core;

/// <summary>
///     A component a hand-written chain step writes state onto that no prop holds — <c>.Choices(rows)</c> on a select —
///     so the generated replay of a <c>Key</c> claim cannot carry it.
/// </summary>
/// <remarks>
///     A step written before <c>Key</c> runs on the instance the entry handed over; when the key keeps another, the
///     generated copy replays the props (#1118) and this hands over the rest. Without it a select keyed after its rows
///     drew the rows of the render that first filed it, for good: TA's pickers read their rows after the first render
///     and opened on "No results found" (2026-11-11).
/// </remarks>
internal interface ICarriesHandSteps
{
    /// <summary>Writes onto <paramref name="kept" /> what this render's chain wrote here by hand.</summary>
    /// <param name="kept">The instance the key keeps, of this one's type, its props already replayed.</param>
    void CarryTo(Component kept);
}
