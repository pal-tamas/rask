using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     A member a day picker is bound to that holds the day as something else: a <c>DateTime</c> or a
///     <c>DateTime?</c>, a <see cref="Rask.Values.Day" /> or a <see cref="Rask.Values.Moment" /> value object
///     (<see cref="UiDayMembers" />). The member is the field — its label, its rules, its messages — and is written in
///     its own type: a new day keeps the time of day the member held, and a value object's rules are asked as it is made.
/// </summary>
/// <typeparam name="TMember">The member's type.</typeparam>
internal sealed class UiDayMember<TMember> : UiPickerBinding<DateOnly>
{
    private readonly LambdaExpression _bind;
    private readonly Func<TMember?, DateOnly?> _day;
    private readonly Func<TMember?, DateOnly, (TMember? Value, string? Refused)> _onDay;
    private readonly bool _holdsNone;

    /// <summary>A member read as its day by <paramref name="day" /> and written by <paramref name="onDay" />.</summary>
    /// <param name="bind">The member.</param>
    /// <param name="day">The day the member holds; nothing when it holds none.</param>
    /// <param name="onDay">What the member holds on another day, made from what it held; or why its rules refuse that.</param>
    /// <param name="holdsNone">Whether the member can hold nothing: emptied, it is null; else it says it must be given.</param>
    /// <param name="bounds">The earliest and the latest day the member's own rules take.</param>
    internal UiDayMember(
        Expression<Func<TMember?>> bind,
        Func<TMember?, DateOnly?> day,
        Func<TMember?, DateOnly, (TMember? Value, string? Refused)> onDay,
        bool holdsNone,
        (DateOnly? Min, DateOnly? Max) bounds = default)
    {
        (_bind, _day, _onDay, _holdsNone) = (bind, day, onDay, holdsNone);
        Bounds = bounds;
    }

    /// <inheritdoc />
    internal override (DateOnly? Min, DateOnly? Max) Bounds { get; }

    /// <inheritdoc />
    internal override LambdaExpression Bound(IFormControl<DateOnly> control) => _bind;

    /// <inheritdoc />
    internal override (ExpressionAccessor.Accessor? Accessor, EditContext? Context, DateOnly Current) Resolve(IFormControl<DateOnly> control)
    {
        var accessor = ExpressionAccessor.Parse(_bind);
        var context = BindingHelpers.ResolveBindingContext(accessor.Target);
        if (context is not null)
        {
            // The member's own rules, with the picker's `.Validate(…)` asked of the day it holds; every render, as
            // IFormControl<T>.RegisterValidator does, so a rule dropped goes.
            context.RegisterFieldValidator(
                accessor.Field, RaskValidation.RuleFor<TMember>(accessor.Field, Asked(control.Validate), accessor.UnderOptional), accessor.Getter);
            context.TrackBindingOwner(accessor.Field, accessor.Owner as Component ?? BindingConsumerRegistry.Resolve(control));
        }

        return (accessor, context, _day(Held(control, accessor)) ?? default);
    }

    /// <inheritdoc />
    internal override Task CommitAsync(
        IFormControl<DateOnly> control, ExpressionAccessor.Accessor? accessor, EditContext? context, DateOnly value)
    {
        // A picker emptied hands over no day: the member holds none.
        if (value == default || accessor is null)
        {
            return ClearAsync(control, accessor, context);
        }

        var (made, refused) = _onDay(Held(control, accessor), value);
        return refused is { } said
            ? UnreadAsync(control, accessor, context, value, said)
            : UiFormCommit.CommitAsync(control, accessor, context, value, made);
    }

    /// <inheritdoc />
    internal override Task ClearAsync(IFormControl<DateOnly> control, ExpressionAccessor.Accessor? accessor, EditContext? context) =>
        _holdsNone
            ? UiFormCommit.CommitAsync(control, accessor, context, default, null)
            : UnreadAsync(control, accessor, context, default, Rules.Required);

    /// <inheritdoc />
    internal override Task UnreadAsync(
        IFormControl<DateOnly> control, ExpressionAccessor.Accessor? accessor, EditContext? context, DateOnly shown, string said) =>
        UiFormCommit.UnreadHeldAsync(control, accessor, context, accessor is null ? null : Held(control, accessor), said);

    // What the member holds now: the model's, or what the reader chose and the session still holds.
    private static TMember? Held(IFormControl<DateOnly> control, ExpressionAccessor.Accessor accessor)
    {
        var held = control is Component drawn && PendingBinds.TryPeek(drawn, accessor.Field, out var chosen) ? chosen : accessor.Getter();
        return held is TMember member ? member : default;
    }

    // The picker's own rule of the day, asked of the day the member holds; an async one reads the field's token from
    // the ambient scope the form's check opens, as it would bound to a DateOnly.
    private Delegate? Asked(Validator<DateOnly>? validate) => validate?.Rule switch
    {
        Validate<DateOnly> rule => new Validate<TMember?>(member => rule(_day(member) ?? default)),
        Func<DateOnly, ValueTask<IEnumerable<string>>> rule => new Func<TMember?, ValueTask<IEnumerable<string>>>(member => rule(_day(member) ?? default)),
        _ => null,
    };
}
