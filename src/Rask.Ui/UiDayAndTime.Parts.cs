using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

// Bound to two members (#1362): `Ui.DayAndTime.Bind(() => m.Day, () => m.Time)`, a DateOnly? and a TimeOnly? the model
// keeps apart. The same two pickers in the same row, each over its own member: each part writes its member as the one
// moment is written (with the next action, or at once for `.Live()`), and says its member's messages under itself.
// The time waits for a day, as the one moment's does; a day taken away empties the time and disables it again.
public sealed partial class UiDayAndTime<T> : ICarriesHandSteps
{
    // Each part's column: the picker with its message under it.
    private const string Fill = "w-full";

    // What `.Bind(day, time)` bound, kept as `.Choices(…)` is: no prop holds it. Ignored once a render binds one member.
    private UiDayAndTimeParts? _parts;

    // The pickers the last render drew: each holds what its part chose until it is written (PendingBinds keeps one
    // member per control), and is drawn again as the same instance.
    private UiDatePicker? _dayPart;
    private UiTimePicker<TimeOnly?>? _timePart;

    // The two members as the last render resolved them, for the handlers.
    private (ExpressionAccessor.Accessor Day, ExpressionAccessor.Accessor Time, EditContext? Context)? _bound;

    private UiDayAndTimeParts? Parts => Bind is null ? _parts : null;

    /// <summary>Binds the day to <paramref name="day" /> and the time to <paramref name="time" />.</summary>
    internal UiDayAndTime<T> BindParts(Expression<Func<DateOnly?>> day, Expression<Func<TimeOnly?>> time)
    {
        _parts = new UiDayAndTimeParts(day, time);
        BuilderRuntime.MarkChanged(this);
        return this;
    }

    /// <inheritdoc />
    void ICarriesHandSteps.CarryTo(Component kept)
    {
        if (kept is UiDayAndTime<T> other && _parts is { } mine)
        {
            other._parts = mine;
        }
    }

    private Component RenderParts(UiDayAndTimeParts parts)
    {
        // The pair's label and description; each part says its own messages.
        var field = UiWithField.For(this, saysMessages: false);
        var (dayAt, timeAt) = (ExpressionAccessor.Parse(parts.Day), ExpressionAccessor.Parse(parts.Time));
        var context = BindingHelpers.ResolveBindingContext(dayAt.Target);
        _bound = (dayAt, timeAt, context);
        var day = Held<DateOnly?>(_dayPart, dayAt);
        var time = Held<TimeOnly?>(_timePart, timeAt);
        var locked = Disabled == true;
        var dayId = field.ControlId;
        var timeId = dayId + "-time";

        _dayPart = Ui.DatePicker
            .Value(day ?? default)
            .OnChange(PartDayAsync)
            .Typed()
            .Key("day")
            .Id(dayId)
            .Locale(Locale)
            .Live(Live)
            .Clearable(UiChoices.MayBeEmpty(parts.Day, typeof(DateOnly?)))
            .Disabled(locked)
            .Invalid(Invalid == true || IsInvalid(context, dayAt))
            .Class(Fill);
        _timePart = Ui.TimePicker
            .Value(time)
            .OnChange(PartTimeAsync)
            .Type(Ui.TimePickerType.Input)
            .Key("time")
            .Id(timeId)
            .Locale(Locale)
            .Live(Live)
            .Disabled(locked || day is null)
            .Invalid(Invalid == true || IsInvalid(context, timeAt))
            .Class(Fill);

        return field.Wrap(Div.Class(Row, Class).Data(Marks)[
            Div.Key("day-part").Class(DayWidth)[
                Context.Provide(new UiUnreadRelay(said => PartUnreadAsync(_dayPart, dayAt, context, day, said)))[_dayPart],
                Message(dayId, parts.Day)],
            Div.Key("time-part").Class(TimeWidth(Culture))[
                Context.Provide(new UiUnreadRelay(said => PartUnreadAsync(_timePart, timeAt, context, time, said)))[_timePart],
                Message(timeId, parts.Time)]]);
    }

    private UiError? Message(string partId, LambdaExpression member) =>
        ShowValidation == false ? null : Ui.Error.Id(UiFieldId.Error(partId)).For(member);

    private static bool IsInvalid(EditContext? context, ExpressionAccessor.Accessor member) => context?.IsInvalid(member.Field) == true;

    // What the part shows: what it chose and the session still holds, or the model's.
    private static TPart? Held<TPart>(Component? part, ExpressionAccessor.Accessor member)
    {
        var shown = part is not null && PendingBinds.TryPeek(part, member.Field, out var chosen) ? chosen : member.Getter();
        return shown is TPart value ? value : default;
    }

    private Task PartDayAsync(DateOnly picked)
    {
        if (_bound is not { } bound || UiRefusals.Locked(this, bound.Day))
        {
            return Task.CompletedTask;
        }

        if (picked != default)
        {
            return PartWriteAsync(_dayPart, bound.Day, bound.Context, (DateOnly?)picked);
        }

        return PartDayTakenAsync(bound.Day, bound.Time, bound.Context);
    }

    // No day is no moment: the time goes with it, and waits for the next day.
    private async Task PartDayTakenAsync(ExpressionAccessor.Accessor day, ExpressionAccessor.Accessor time, EditContext? context)
    {
        await PartWriteAsync(_dayPart, day, context, (DateOnly?)null).ConfigureAwait(false);
        if (Held<TimeOnly?>(_timePart, time) is not null)
        {
            await PartWriteAsync(_timePart, time, context, (TimeOnly?)null).ConfigureAwait(false);
        }
    }

    private Task PartTimeAsync(TimeOnly? picked) =>
        _bound is not { } bound || UiRefusals.Locked(this, bound.Time)
            ? Task.CompletedTask
            : PartWriteAsync(_timePart, bound.Time, bound.Context, picked);

    // A part's member, written as a bound control writes its own: with the next action, or at once for `.Live()`.
    private Task PartWriteAsync<TPart>(Component? part, ExpressionAccessor.Accessor member, EditContext? context, TPart? value)
    {
        ReaderEdits.Note(this);
        var holder = part ?? this;
        var written = PendingBinds.Commit(holder, member.Field, context, value, Write, SettleAsync);
        StateHasChanged();
        return written;

        bool Write()
        {
            member.Setter(value);
            return true;
        }

        async Task SettleAsync()
        {
            await BindingHelpers.NotifyAndValidateField(context, member.Field).ConfigureAwait(false);
            await ((IFormControl<T>)this).InvokeAfterBind(UiDayAndTimeValue.As<T>(PartsMoment())).ConfigureAwait(false);
        }
    }

    // A part left half typed: its member keeps what it held, and the form says so under that part.
    private Task PartUnreadAsync<TPart>(Component? part, ExpressionAccessor.Accessor member, EditContext? context, TPart? shown, string said)
    {
        if (UiRefusals.Locked(this, member))
        {
            return Task.CompletedTask;
        }

        ReaderEdits.Note(this);
        if (context is null)
        {
            return Task.CompletedTask;
        }

        var noted = PendingBinds.Commit(part ?? this, member.Field, context, shown, Note, static () => Task.CompletedTask);
        StateHasChanged();
        return noted;

        bool Note()
        {
            context.NoteUnread(member.Field, said);
            return false;
        }
    }

    // The moment the two members make, as AfterBind hears it: the day at its time, its first minute without one.
    private DateTime? PartsMoment()
    {
        if (_bound is not { } bound || bound.Day.Getter() is not DateOnly day)
        {
            return null;
        }

        return day.ToDateTime(bound.Time.Getter() as TimeOnly? ?? TimeOnly.MinValue);
    }
}
