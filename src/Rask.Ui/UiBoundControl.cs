using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

// The one control a bound field draws for a member that is not a range: chosen by the member's shape, else its type,
// and handed the field's words. Every control here labels, badges and says its message itself (UiWithField).
internal static class UiBoundControl
{
    internal static Component Draw<T>(
        Expression<Func<T>> bind, FieldShape? shape, UiBoundLook look, Action<UiSelect<T>>? choices)
    {
        var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (choices is not null)
        {
            return Picked(bind, look, choices);
        }

        return (shape?.Kind ?? KindOf(type)) switch
        {
            FieldKind.Id => throw UiBoundMember.NoChoices(bind),
            FieldKind.Choice => Chosen(bind, look, shape?.Options ?? OptionsOf(type)),
            FieldKind.Day => Day(bind, shape, look),
            FieldKind.Number or FieldKind.Whole => Number(bind, shape!, look),
            _ when type == typeof(bool) => Checked(bind, look),
            _ when type == typeof(DateTime) || type == typeof(DateTimeOffset) => Moment(bind, look),
            _ => Text(bind, look),
        };
    }

    /// <summary>A number input's attributes for a shape: its bounds, and its step from the scale.</summary>
    internal static UiInput<T> Numbered<T>(UiInput<T> input, FieldShape? shape) =>
        input.Type(InputType.Number).Min(Said(shape?.Min)).Max(Said(shape?.Max)).Step(Step(shape));

    /// <summary>The field's words and state on an input, plain text or number.</summary>
    internal static UiInput<T> Input<T>(Expression<Func<T>> bind, UiBoundLook look) =>
        Ui.Input.Bind(bind)
            .Label(look.Label)
            .Description(look.Description)
            .Placeholder(look.Placeholder)
            .ShowValidation(look.ShowValidation)
            .Disabled(look.Disabled)
            .ReadOnly(look.ReadOnly)
            .Live(look.Timing.Live)
            .Blur(look.Timing.Blur)
            .Debounce(look.Timing.Debounce)
            .Badged(look.Badge);

    /// <summary>A day picker over a member of <c>DateOnly</c> or <c>DateOnly?</c>, latest today where the shape says so.</summary>
    internal static UiDatePicker DayPicker(LambdaExpression bind, FieldShape? shape) =>
        (bind switch
        {
            Expression<Func<DateOnly>> day => Ui.DatePicker.Bind(day),
            Expression<Func<DateOnly?>> maybe => Ui.DatePicker.Bind(maybe),
            _ => throw UiBoundMember.NoControlFor(bind, bind.ReturnType),
        }).Max(shape?.NotAfterToday == true ? Clock.Today : null);

    // What a member says by its type alone, where no shape was registered for it.
    private static FieldKind? KindOf(Type type)
    {
        if (type == typeof(DateOnly))
        {
            return FieldKind.Day;
        }

        return type.IsEnum ? FieldKind.Choice : null;
    }

    private static Component Text<T>(Expression<Func<T>> bind, UiBoundLook look) =>
        look.Multiline
            ? Ui.Textarea.Bind(bind)
                .Label(look.Label)
                .Description(look.Description)
                .Placeholder(look.Placeholder)
                .Badge(look.Badge)
                .ShowValidation(look.ShowValidation)
                .Disabled(look.Disabled)
                .ReadOnly(look.ReadOnly)
                .Live(look.Timing.Live)
                .Blur(look.Timing.Blur)
                .Debounce(look.Timing.Debounce)
            : Input(bind, look).Type(UiBoundMember.TextType(bind));

    private static UiInput<T> Number<T>(Expression<Func<T>> bind, FieldShape shape, UiBoundLook look) =>
        Numbered(Input(bind, look), shape);

    private static UiDatePicker Day<T>(Expression<Func<T>> bind, FieldShape? shape, UiBoundLook look) =>
        DayPicker(bind, shape)
            .Label(look.Label)
            .Description(look.Description)
            .Placeholder(look.Placeholder)
            .Badge(look.Badge)
            .ShowValidation(look.ShowValidation)
            .Disabled(look.Locked)
            .Live(look.Timing.Live);

    private static UiDayAndTime<T> Moment<T>(Expression<Func<T>> bind, UiBoundLook look) =>
        Ui.DayAndTime.Bind(bind)
            .Label(look.Label)
            .Description(look.Description)
            .Badge(look.Badge)
            .ShowValidation(look.ShowValidation)
            .Disabled(look.Locked)
            .Live(look.Timing.Live);

    private static UiCheckbox Checked<T>(Expression<Func<T>> bind, UiBoundLook look) =>
        Ui.Checkbox.Bind(bind switch
        {
            Expression<Func<bool?>> maybe => maybe,
            _ => Expression.Lambda<Func<bool?>>(Expression.Convert(bind.Body, typeof(bool?)), bind.Parameters),
        })
            .Label(look.Label)
            .Description(look.Description)
            .ShowValidation(look.ShowValidation)
            .Disabled(look.Locked)
            .Live(look.Timing.Live);

    private static UiSelect<T> Select<T>(Expression<Func<T>> bind, UiBoundLook look) =>
        Ui.Select.Bind(bind)
            .Label(look.Label)
            .Description(look.Description)
            .Placeholder(look.Placeholder)
            .Badge(look.Badge)
            .ShowValidation(look.ShowValidation)
            .Disabled(look.Locked)
            .Live(look.Timing.Live);

    private static UiSelect<T> Picked<T>(Expression<Func<T>> bind, UiBoundLook look, Action<UiSelect<T>> choices)
    {
        var select = Select(bind, look);
        choices(select);
        return select;
    }

    // The options in their declared order — an enum's order is its meaning — with an empty one first where the member
    // may hold none.
    private static Component Chosen<T>(Expression<Func<T>> bind, UiBoundLook look, IReadOnlyList<FieldOption> options)
    {
        var none = Nullable.GetUnderlyingType(typeof(T)) is not null ? Ui.SelectOption.Key("").Value(null)[""] : null;
        return Select(bind, look)[
            none,
            options.Select(option => Ui.SelectOption.Key(option.Value).Value(option.Value)[option.Label])
        ];
    }

    // An enum no model registered a shape for: its members by name.
    private static FieldOption[] OptionsOf(Type type) =>
        [.. Enum.GetValuesAsUnderlyingType(type).Cast<object>()
            .Select(value => Enum.ToObject(type, value))
            .Select(member => new FieldOption(member, Enum.GetName(type, member) ?? member.ToString() ?? ""))];

    private static string? Said(decimal? bound) => bound?.ToString(CultureInfo.InvariantCulture);

    // 10^-Scale: a whole number steps by one, a number of two decimals by 0.01; no scale leaves the input's own step.
    private static string? Step(FieldShape? shape) => shape switch
    {
        { Kind: FieldKind.Whole } => "1",
        { Scale: 0 } => "1",
        { Scale: { } scale } => "0." + new string('0', scale - 1) + "1",
        _ => null,
    };
}
