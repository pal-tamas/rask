namespace Rask;

/// <summary>
///     What a select's or a pillbox's <c>.Choices(…)</c> made, and the step that made it: kept so that a <c>Key</c>
///     written after it hands the step to the instance the key keeps (<see cref="ICarriesHandSteps" />), and so that a
///     <c>.Leading(…)</c> after it makes the rows again with the leading ones before them.
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
    private Func<UiChosen?, object?, (UiChosen Chosen, bool Changed)>? _step;

    // The last `.Choices(…)` taken, and what the render before it had made: a `.Leading(…)` runs it again over that.
    private Func<UiChosen?, object?, (UiChosen Chosen, bool Changed)>? _made;
    private UiChosen? _before;

    // Whether this render's chain took `.Choices(…)`, and `.Leading(…)` after it.
    private bool _chained;
    private bool _led;

    /// <summary>What the last <c>.Choices(…)</c> made; null before the first.</summary>
    internal UiChosen? Chosen { get; private set; }

    /// <summary>
    ///     The rows <c>.Leading(…)</c> pins before the choices, as the <c>Ui.Choice&lt;T&gt;[]</c> they were handed; none
    ///     without one. The last render's until this one's chain says otherwise, so a chain written the same way twice
    ///     makes the same rows.
    /// </summary>
    internal object? Leading { get; private set; }

    /// <summary>Runs <paramref name="step" /> over what the last render made, and tells the render cache when it is not that.</summary>
    internal void Take(Component owner, Func<UiChosen?, object?, (UiChosen Chosen, bool Changed)> step)
    {
        _step = step;
        _made = step;
        _before = Chosen;
        _chained = true;
        _led = false;
        Run(owner);
    }

    /// <summary>Pins <paramref name="rows" /> before the choices: makes them again, the leading rows first.</summary>
    /// <exception cref="InvalidOperationException">No <c>.Choices(…)</c> came before it.</exception>
    internal void Lead(Component owner, object rows, string control)
    {
        if (_made is null || !_chained)
        {
            throw new InvalidOperationException(
                $"{control}'s .Leading(…) pins rows before the choices, so it follows .Choices(…): {control}.Bind(…).Choices(rows).Leading(none).");
        }

        Leading = rows;
        _led = true;
        Run(owner);
    }

    /// <summary>Hands this render's step to <paramref name="kept" />, the instance a key kept instead of this one's owner.</summary>
    internal void CarryTo(Component kept, UiChoosing other)
    {
        if (_step is { } step)
        {
            _step = null;
            other.Leading = Leading;
            other.Take(kept, step);
            other._led = _led;
        }
    }

    /// <summary>The owner is drawing: what its chain wrote is its own now, and no later claim hands it on.</summary>
    internal void Drawn()
    {
        _step = null;

        // This render's chain took `.Choices(…)` and no `.Leading(…)`: the last render's leading rows are gone.
        if (_chained && !_led && Leading is not null)
        {
            Leading = null;
            Run(owner: null);
        }

        _chained = false;
    }

    private void Run(Component? owner)
    {
        var (chosen, changed) = _made!(_before, Leading);
        if (changed && owner is not null)
        {
            BuilderRuntime.MarkChanged(owner);
        }

        Chosen = chosen;
    }
}
