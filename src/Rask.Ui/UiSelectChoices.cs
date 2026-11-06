namespace Rask;

/// <summary>
///     <c>.Choices(…)</c> on a select bound to a member that may hold no value — <c>int?</c>, a <c>Guid?</c> — whose rows
///     hold the value itself: <c>Ui.Select.Bind(() =&gt; model.CompanyId).Choices(companies)</c> over
///     <c>Ui.Choice&lt;int&gt;</c> rows.
/// </summary>
/// <remarks>
///     <see cref="UiSelect{T}.Choices{TValue}(IEnumerable{Ui.Choice{TValue}})" /> at the call site: C# cannot say that a
///     select of <c>int?</c> takes rows of <c>int</c> on the select itself.
/// </remarks>
public static class UiSelectChoices
{
    /// <inheritdoc cref="UiSelect{T}.Choices{TValue}(IEnumerable{Ui.Choice{TValue}})" />
    /// <param name="select">The select, opened on a member of <c>T?</c>.</param>
    /// <param name="choices">The rows, in any order: <c>new Ui.Choice&lt;int&gt;(c.Id, c.Name)</c>.</param>
    /// <typeparam name="T">The type of a row's value.</typeparam>
    public static UiSelect<T?> Choices<T>(this UiSelect<T?> select, IEnumerable<Ui.Choice<T>> choices)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(select);
        return select.Offer(choices);
    }

    /// <inheritdoc cref="UiSelect{T}.Choices{TValue}(Func{string, Task{IReadOnlyList{Ui.Choice{TValue}}}}, Ui.Choice{TValue}?)" />
    /// <param name="select">The select, opened on a member of <c>T?</c>.</param>
    /// <param name="find">The rows for what was typed, trimmed: the first few for nothing typed.</param>
    /// <param name="held">The row the value holds, so the select names it before anything is found.</param>
    /// <typeparam name="T">The type of a row's value.</typeparam>
    public static UiSelect<T?> Choices<T>(this UiSelect<T?> select, Func<string, Task<IReadOnlyList<Ui.Choice<T>>>> find, Ui.Choice<T>? held = null)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(select);
        return select.Offer(find, held);
    }
}
