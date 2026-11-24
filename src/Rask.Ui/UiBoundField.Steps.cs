namespace Rask;

// The step no property holds: the rows an id is picked from.
public sealed partial class UiBoundField<T> : ICarriesHandSteps
{
    private Action<UiSelect<T>>? _choices;

    /// <summary>
    ///     The rows another row's id is picked from — the shapes <c>Ui.Select.Choices</c> takes: <c>.Choices(vehicles)</c>
    ///     over <c>Ui.Choice&lt;VehicleId&gt;</c> rows. A field holding an id has no control without them.
    /// </summary>
    /// <param name="choices">The rows, in any order: <c>new Ui.Choice&lt;VehicleId&gt;(v.Id, v.Plate)</c>.</param>
    /// <typeparam name="TValue">The type of a row's value.</typeparam>
    /// <returns>This field.</returns>
    public UiBoundField<T> Choices<TValue>(IEnumerable<Ui.Choice<TValue>> choices)
        where TValue : T => Choose(select => select.Offer(choices));

    /// <summary>The rows another row's id is picked from, found for what the reader types: <c>.Choices(Vehicle.Find)</c>.</summary>
    /// <param name="find">The rows for what was typed, trimmed: the first few for nothing typed.</param>
    /// <param name="held">The row the value holds, so the select names it before anything is found.</param>
    /// <typeparam name="TValue">The type of a row's value.</typeparam>
    /// <returns>This field.</returns>
    public UiBoundField<T> Choices<TValue>(Func<string, Task<IReadOnlyList<Ui.Choice<TValue>>>> find, Ui.Choice<TValue>? held = null)
        where TValue : T => Choose(select => select.Offer(find, held));

    /// <summary>The rows another row's id is picked from, found a page at a time for what the reader types.</summary>
    /// <param name="find">The page of rows for what was typed, trimmed: <c>query.Skip(page.Skip).Take(page.Take)</c>.</param>
    /// <param name="held">The row the value holds, so the select names it before anything is found.</param>
    /// <typeparam name="TValue">The type of a row's value.</typeparam>
    /// <returns>This field.</returns>
    public UiBoundField<T> Choices<TValue>(
        Func<string, Ui.ChoicePage, Task<IReadOnlyList<Ui.Choice<TValue>>>> find, Ui.Choice<TValue>? held = null)
        where TValue : T => Choose(select => select.Offer(find, held));

    /// <summary>The rows for a field bound to a member of <c>T?</c>: the kit's own step, for <see cref="UiBoundFieldChoices" />.</summary>
    /// <param name="offer">What to hand the select.</param>
    internal UiBoundField<T> Choose(Action<UiSelect<T>> offer)
    {
        _choices = offer;
        BuilderRuntime.MarkChanged(this);
        return this;
    }

    /// <inheritdoc />
    void ICarriesHandSteps.CarryTo(Component kept)
    {
        if (kept is UiBoundField<T> other)
        {
            other._choices = _choices ?? other._choices;
        }
    }
}
