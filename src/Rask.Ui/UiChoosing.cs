namespace Rask;

/// <summary>
///     What a select's or a pillbox's <c>.Choices(…)</c> made, and the step that made it: kept so that a <c>Key</c>
///     written after it hands the step to the instance the key keeps (<see cref="ICarriesHandSteps" />).
/// </summary>
/// <remarks>
///     <c>.Choices(…)</c> is no prop, so the generated replay of a key claim cannot carry it. Written before the key —
///     <c>Ui.Select.Bind(f).Choices(rows).Key(…)</c> — the rows landed on the instance the key set aside, and the kept
///     one drew the rows of the render that first filed it for good: a picker whose rows the page read after its first
///     render opened on "No results found" (TA, 2026-11-11).
/// </remarks>
internal sealed class UiChoosing
{
    // This render's `.Choices(…)` as a step over what the last render made; null once handed on, or drawn.
    private Func<UiChosen?, (UiChosen Chosen, bool Changed)>? _step;

    /// <summary>What the last <c>.Choices(…)</c> made; null before the first.</summary>
    internal UiChosen? Chosen { get; private set; }

    /// <summary>Runs <paramref name="step" /> over what the last render made, and tells the render cache when it is not that.</summary>
    internal void Take(Component owner, Func<UiChosen?, (UiChosen Chosen, bool Changed)> step)
    {
        _step = step;
        var (chosen, changed) = step(Chosen);
        if (changed)
        {
            BuilderRuntime.MarkChanged(owner);
        }

        Chosen = chosen;
    }

    /// <summary>Hands this render's step to <paramref name="kept" />, the instance a key kept instead of this one's owner.</summary>
    internal void CarryTo(Component kept, UiChoosing other)
    {
        if (_step is { } step)
        {
            _step = null;
            other.Take(kept, step);
        }
    }

    /// <summary>The owner is drawing: what its chain wrote is its own now, and no later claim hands it on.</summary>
    internal void Drawn() => _step = null;
}
