using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Rask.Core.Forms;
using static Rask.Markup;

namespace Rask;

// A range drawn by a bound field: two controls of the ends' kind under one label, each bound to its end of the range
// member (`() => m.Km.Start`, `() => m.Km.End`) and each saying its own message — the generated model's live end rule
// files a backwards end under Km.End.
internal static class UiBoundRange
{
    private const string Ends = "grid grid-cols-2 gap-3";

    // One control per end type: a number input, a day picker, a moment.
    private static readonly Dictionary<Type, Func<End, Component>> Controls = new()
    {
        [typeof(int)] = Number<int>,
        [typeof(int?)] = Number<int?>,
        [typeof(long)] = Number<long>,
        [typeof(long?)] = Number<long?>,
        [typeof(decimal)] = Number<decimal>,
        [typeof(decimal?)] = Number<decimal?>,
        [typeof(double)] = Number<double>,
        [typeof(double?)] = Number<double?>,
        [typeof(DateOnly)] = Day,
        [typeof(DateOnly?)] = Day,
        [typeof(DateTime)] = Moment<DateTime>,
        [typeof(DateTime?)] = Moment<DateTime?>,
        [typeof(DateTimeOffset)] = Moment<DateTimeOffset>,
        [typeof(DateTimeOffset?)] = Moment<DateTimeOffset?>,
    };

    internal static Component Draw(Component owner, LambdaExpression bind, FieldShape shape, UiBoundLook look, Action<Component> tune)
    {
        var id = UiFieldId.Derive(owner, null, bind, look.Label);
        var ends = shape with { Kind = shape.Ends ?? FieldKind.Number };
        var start = Control(new End(bind, EndOf(bind, shape.Start ?? throw UiBoundMember.NoEnds(bind)), id + "-start", ends, look), tune);
        var end = Control(new End(bind, EndOf(bind, shape.End ?? throw UiBoundMember.NoEnds(bind)), id + "-end", ends, look), tune);

        return UiWithField.For(new UiBoundRangeField(id + "-start", bind, look), saysMessages: false)
            .Wrap(Div.Class(Ends).Data("ui-field-range", "")[start, end]);
    }

    private static Component Control(End end, Action<Component> tune)
    {
        var control = Controls.TryGetValue(end.Member.Type, out var draw)
            ? draw(end)
            : throw UiBoundMember.NoControlFor(end.Range, end.Member.Type);
        tune(control);

        // Each end in a field of its own, so its message is said under it and its control is described by it.
        return Ui.Field[control, end.Look.ShowValidation == false ? null : Ui.Error];
    }

    // The end's member on the range the field is bound to. Its Start and End stay in a trimmed app: the generated
    // model's own end rules read them.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "A range's ends are read by the generated model's own rules, so the trimmer keeps them.")]
    private static MemberExpression EndOf(LambdaExpression bind, string member)
    {
        var range = bind.Body;
        while (range is UnaryExpression { NodeType: ExpressionType.Convert } convert)
        {
            range = convert.Operand;
        }

        return Expression.Property(range, member);
    }

    private static UiInput<T> Number<T>(End end) =>
        UiBoundControl.Numbered(UiBoundControl.Input(end.Bind<T>(), end.Look with { Label = null, Description = null }), end.Shape)
            .Id(end.Id);

    private static UiDatePicker Day(End end) =>
        UiBoundControl.DayPicker(end.Member.Type == typeof(DateOnly) ? end.Bind<DateOnly>() : end.Bind<DateOnly?>(), end.Shape)
            .Id(end.Id)
            .Placeholder(end.Look.Placeholder)
            .ShowValidation(end.Look.ShowValidation)
            .Disabled(end.Look.Locked)
            .Live(end.Look.Timing.Live);

    private static UiDayAndTime<T> Moment<T>(End end) =>
        Ui.DayAndTime.Bind(end.Bind<T>())
            .Id(end.Id)
            .ShowValidation(end.Look.ShowValidation)
            .Disabled(end.Look.Locked)
            .Live(end.Look.Timing.Live);

    // One end of the range: its member, the control's id, and the range's shape with the ends' kind.
    private sealed record End(LambdaExpression Range, MemberExpression Member, string Id, FieldShape Shape, UiBoundLook Look)
    {
        internal Expression<Func<T>> Bind<T>() => Expression.Lambda<Func<T>>(Member);
    }
}
