using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Rask.Core.Forms;
using Rask.Values;

namespace Rask;

// The member a table's column or cell is bound to (`Ui.TableColumn.For<OrderRead>(o => o.Total)`,
// `Ui.TableCell.For(() => order.Total)`): what it is called, whether a list shows it, the edge its values keep to, and
// its value written for the reader. Both read the same attributes and shape, so a heading and its cells agree.
internal sealed class UiTableMember
{
    private const string Nothing = "–";

    private readonly FieldShape? _shape;
    private readonly PropertyInfo _property;

    private UiTableMember(Type owner, PropertyInfo property)
    {
        _property = property;
        _shape = RaskValidation.ShapeOf(owner, property);
    }

    /// <summary>What the column is called: the member's <c>[Display(Name)]</c>, else its name.</summary>
    internal string Label => DisplayNames.Of(_property) ?? _property.Name;

    /// <summary>False for <c>[ScaffoldColumn(false)]</c>: neither the heading nor a cell of it is drawn.</summary>
    internal bool Listed => _shape?.Listed ?? true;

    /// <summary>The end for a number or a whole number, as Flux aligns an amount; null for the start.</summary>
    internal Ui.Align? Align => _shape is { Kind: FieldKind.Number or FieldKind.Whole } || IsNumber(_property.PropertyType)
        ? Ui.Align.End
        : null;

    /// <summary>The member a heading names: <c>o =&gt; o.Total</c>, read off the row's type with no row in hand.</summary>
    internal static UiTableMember Named(LambdaExpression member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var body = Unwrapped(member.Body);
        return body is MemberExpression { Member: PropertyInfo property, Expression: { } holder }
            ? new UiTableMember(holder.Type, property)
            : throw new ArgumentException($"Ui.TableColumn.For needs a property of the row — r => r.Name. Got: {member}", nameof(member));
    }

    /// <summary>The member a cell shows, and its value now: <c>() =&gt; order.Total</c>.</summary>
    internal static (UiTableMember Member, object? Value) Bound(LambdaExpression member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var bound = ExpressionAccessor.Parse(member);
        return (new UiTableMember(bound.Target.GetType(), bound.Property), bound.Getter());
    }

    /// <summary>
    ///     <paramref name="value" /> written for the reader, in their culture: the member's <c>[DisplayFormat]</c>, else
    ///     its kind — a number with its scale, a day, a moment on the wall clock — an enum's label, a <c>bool</c>'s yes or
    ///     no, a value object's value. No value is its <c>NullDisplayText</c>, else "–".
    /// </summary>
    internal string Text(object? value)
    {
        var held = Unwrapped(value);
        if (held is null)
        {
            return _shape?.NullText ?? Nothing;
        }

        var culture = RaskCulture.Current;
        if (_shape?.Format is { } format)
        {
            return string.Format(culture, format, held);
        }

        return held switch
        {
            bool yes => yes ? RaskStrings.Get(RaskString.TableCellYes, "Yes") : RaskStrings.Get(RaskString.TableCellNo, "No"),
            Enum choice => Labelled(choice),
            DateOnly day => Format.Day(day),
            DateTime moment => _shape?.Kind == FieldKind.Day ? Format.Day(moment) : Format.Moment(moment),
            DateTimeOffset moment => _shape?.Kind == FieldKind.Day ? Format.Day(moment) : Format.Moment(moment),
            decimal number => _shape?.Scale is { } scale ? Format.Number(number, scale) : Format.Number(number),
            double number => _shape?.Scale is { } scale ? Format.Number(number, scale) : Format.Number(number),
            float number => _shape?.Scale is { } scale ? Format.Number(number, scale) : Format.Number(number),
            byte or sbyte or short or ushort or int or uint or long => Format.Number(Convert.ToInt64(held, CultureInfo.InvariantCulture)),
            IFormattable formattable => formattable.ToString(null, culture),
            _ => held.ToString() ?? "",
        };
    }

    // A value object of one value is shown as the value it holds.
    private static object? Unwrapped(object? value) => value switch
    {
        Rask.Values.Text text => text.Value,
        Number number => number.Value,
        Whole whole => whole.Value,
        Day day => day.Value,
        Moment moment => moment.Value,
        _ => value,
    };

    // `o => (object)o.Total` names o.Total as much as `o => o.Total` does.
    private static Expression Unwrapped(Expression body) =>
        body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert ? Unwrapped(convert.Operand) : body;

    private static bool IsNumber(Type type) =>
        Type.GetTypeCode(Nullable.GetUnderlyingType(type) ?? type) is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16
            or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    // The option's label where the shape lists them, else the member's [Display(Name)], else its name.
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "An enum's [Display] is read off its field, which the trimmer keeps with the enum.")]
    private string Labelled(Enum choice)
    {
        if (_shape?.Options?.FirstOrDefault(option => Equals(option.Value, choice)) is { } option)
        {
            return option.Label;
        }

        var name = Enum.GetName(choice.GetType(), choice);
        return name is not null && choice.GetType().GetField(name) is { } field ? DisplayNames.Of(field) ?? name : choice.ToString();
    }
}
