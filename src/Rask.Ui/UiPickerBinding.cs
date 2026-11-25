using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     How a date picker reads and writes what it is bound to. This one is its own <c>Bind</c>, a member of the
///     picker's own type, through <see cref="UiFormCommit" />; <see cref="UiDayMember{TMember}" /> is a member that
///     holds the day as something else — a <c>DateTime</c>, a value object.
/// </summary>
/// <typeparam name="T">What the picker chooses.</typeparam>
internal class UiPickerBinding<T>
{
    /// <summary>The picker's own <c>Bind</c>.</summary>
    internal static readonly UiPickerBinding<T> Own = new();

    /// <summary>The member the picker is bound to, or null for a picker the parent owns.</summary>
    internal virtual LambdaExpression? Bound(IFormControl<T> control) => control.Bind;

    /// <summary>The earliest and the latest day the member's own rules take; nothing for the picker's own <c>Bind</c>.</summary>
    internal virtual (DateOnly? Min, DateOnly? Max) Bounds => default;

    /// <inheritdoc cref="UiFormCommit.Resolve{T}(IFormControl{T})" />
    internal virtual (ExpressionAccessor.Accessor? Accessor, EditContext? Context, T? Current) Resolve(IFormControl<T> control) =>
        UiFormCommit.Resolve(control);

    /// <inheritdoc cref="UiFormCommit.CommitAsync{T}(IFormControl{T}, ExpressionAccessor.Accessor?, EditContext?, T)" />
    internal virtual Task CommitAsync(IFormControl<T> control, ExpressionAccessor.Accessor? accessor, EditContext? context, T value) =>
        UiFormCommit.CommitAsync(control, accessor, context, value);

    /// <inheritdoc cref="UiFormCommit.ClearAsync{T}(IFormControl{T}, ExpressionAccessor.Accessor?, EditContext?)" />
    internal virtual Task ClearAsync(IFormControl<T> control, ExpressionAccessor.Accessor? accessor, EditContext? context) =>
        UiFormCommit.ClearAsync(control, accessor, context);

    /// <inheritdoc cref="UiFormCommit.UnreadAsync{T}(IFormControl{T}, ExpressionAccessor.Accessor?, EditContext?, T, string)" />
    internal virtual Task UnreadAsync(
        IFormControl<T> control, ExpressionAccessor.Accessor? accessor, EditContext? context, T? shown, string said) =>
        UiFormCommit.UnreadAsync(control, accessor, context, shown, said);
}
