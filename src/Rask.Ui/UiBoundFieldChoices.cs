namespace Rask;

/// <summary>
///     <c>.Choices(…)</c> on a field bound to a member that may hold no id — a <c>VehicleId?</c>, an <c>int?</c> — whose
///     rows hold the id itself: <c>Ui.Field.Bind(() =&gt; model.Vehicle).Choices(Vehicle.Find)</c> over
///     <c>Ui.Choice&lt;VehicleId&gt;</c> rows.
/// </summary>
/// <remarks>
///     <see cref="UiBoundField{T}.Choices{TValue}(IEnumerable{Ui.Choice{TValue}})" /> at the call site: C# cannot say that
///     a field of <c>int?</c> takes rows of <c>int</c> on the field itself.
/// </remarks>
public static class UiBoundFieldChoices
{
    /// <inheritdoc cref="UiBoundField{T}.Choices{TValue}(IEnumerable{Ui.Choice{TValue}})" />
    /// <param name="field">The field, bound to a member of <c>T?</c>.</param>
    /// <param name="choices">The rows, in any order: <c>new Ui.Choice&lt;int&gt;(c.Id, c.Name)</c>.</param>
    /// <typeparam name="T">The type of a row's value.</typeparam>
    public static UiBoundField<T?> Choices<T>(this UiBoundField<T?> field, IEnumerable<Ui.Choice<T>> choices)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Choose(select => select.Offer(choices));
    }

    /// <inheritdoc cref="UiBoundField{T}.Choices{TValue}(Func{string, Task{IReadOnlyList{Ui.Choice{TValue}}}}, Ui.Choice{TValue}?)" />
    /// <param name="field">The field, bound to a member of <c>T?</c>.</param>
    /// <param name="find">The rows for what was typed, trimmed: the first few for nothing typed.</param>
    /// <param name="held">The row the value holds, so the select names it before anything is found.</param>
    /// <typeparam name="T">The type of a row's value.</typeparam>
    public static UiBoundField<T?> Choices<T>(
        this UiBoundField<T?> field, Func<string, Task<IReadOnlyList<Ui.Choice<T>>>> find, Ui.Choice<T>? held = null)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Choose(select => select.Offer(find, held));
    }

    /// <inheritdoc cref="UiBoundField{T}.Choices{TValue}(Func{string, Ui.ChoicePage, Task{IReadOnlyList{Ui.Choice{TValue}}}}, Ui.Choice{TValue}?)" />
    /// <param name="field">The field, bound to a member of <c>T?</c>.</param>
    /// <param name="find">The page of rows for what was typed, trimmed: <c>query.Skip(page.Skip).Take(page.Take)</c>.</param>
    /// <param name="held">The row the value holds, so the select names it before anything is found.</param>
    /// <typeparam name="T">The type of a row's value.</typeparam>
    public static UiBoundField<T?> Choices<T>(
        this UiBoundField<T?> field, Func<string, Ui.ChoicePage, Task<IReadOnlyList<Ui.Choice<T>>>> find, Ui.Choice<T>? held = null)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Choose(select => select.Offer(find, held));
    }
}
