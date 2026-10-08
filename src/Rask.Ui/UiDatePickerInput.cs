using System.Globalization;
using System.Linq.Expressions;

namespace Rask;

/// <summary>
/// A field to type a date picker's date into — month, day and year — that also opens its calendar. Flux UI's
/// <c>flux:date-picker.input</c>.
/// </summary>
/// <remarks>
/// <para>
/// What <c>Ui.DatePicker.Type(Ui.DatePickerType.Input)</c> draws. Two of them in a range picker's
/// <c>Trigger</c> slot are its start and its end, in that order.
/// </para>
/// <para>
/// Three numeric fields in the locale's own order, typed as one field: a part that can take no further digit
/// moves on, the arrows walk and step the parts (the runtime's <c>data-rask-segments</c>). A whole date is
/// written to the model as soon as the last part has one; the calendar shows it the next time it opens. The
/// icons and the room beside the fields open the calendar; a press in a field only puts the caret there.
/// </para>
/// </remarks>
public sealed partial class UiDatePickerInput : Component, IUiFieldControl
{
    private static readonly Dictionary<string, string?> Marks = new(StringComparer.Ordinal)
    {
        ["ui-control"] = "",
        ["ui-group-target"] = "",
    };

    private const string Iso = "yyyy-MM-dd";

    private static readonly Dictionary<string, string?> MarksInvalid = new(Marks, StringComparer.Ordinal) { ["invalid"] = "" };

    private static readonly Dictionary<string, string?> Inputs = new(StringComparer.Ordinal)
    {
        ["ui-date-inputs"] = "",
        ["rask-segments"] = "",
    };

    private string? _ownId;

    /// <summary>The label drawn above the field, which wraps it in a <c>Ui.Field</c>.</summary>
    public string? Label { get; set; }

    /// <summary>Help text drawn between the label and the field.</summary>
    public string? Description { get; set; }

    /// <summary>How tall the field is.</summary>
    public Ui.DatePickerInputSize? Size { get; set; }

    /// <summary>Prevents the reader from typing or opening the picker.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Draws the field as holding an error.</summary>
    public bool? Invalid { get; set; }

    /// <summary>Extra classes for the field.</summary>
    public string? Class { get; set; }

    /// <summary>The field's id; derived from its label unless set.</summary>
    public string? Id { get; set; }

    string IUiFieldControl.ControlId => Id is null && Label is null
        ? _ownId ??= UiFieldId.Own(UiInstanceCounter.Next())
        : UiFieldId.Derive(Id, null, Label);

    LambdaExpression? IUiFieldControl.Bound => null;

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (Context.Get<UiDatePickerScope>() is not { } scope)
        {
            return null;
        }

        var slot = scope.Claim();
        var field = UiWithField.For(this, Label, Description, invalid: Invalid == true || scope.Invalid);
        var owns = Label is not null || Id is not null;
        var aria = new Dictionary<string, string?>(owns ? field.Aria : scope.Aria, StringComparer.Ordinal)
        {
            ["controls"] = scope.CalendarId,
        };
        if (Label is not null)
        {
            aria["labelledby"] = field.LabelId;
        }

        var locked = Disabled == true || scope.Disabled;
        var group = Div
            .Id(owns ? field.ControlId : scope.ControlId)
            .Class(UiClass.Compose(UiDatePickerLook.Typed, UiDatePickerLook.Height((int)(Size ?? Ui.DatePickerInputSize.Base)), Class))
            .Data(field.Invalid ? MarksInvalid : Marks)
            .Role("group")
            .Aria(aria)[
            Opener(scope, locked, Ui.IconName.Calendar, UiDatePickerLook.Leading),
            Div.Class(UiDatePickerLook.Segments).Data(Inputs).Attributes(("dir", "ltr"))[
                Segments(scope, locked),
                Whole(scope, slot)
            ],
            locked ? Span.Class(UiDatePickerLook.Spacer) : Span.Class(UiDatePickerLook.Spacer).Data("rask-toggle", scope.PopoverId),
            Opener(scope, locked, Ui.IconName.ChevronDown, UiDatePickerLook.Trailing)
        ];

        return field.Wrap(group);
    }

    // The locale's own order and separator: mm/dd/yyyy in the US, dd.mm.yyyy in Germany, yyyy/mm/dd in Japan.
    private static IEnumerable<Component> Segments(UiDatePickerScope scope, bool disabled)
    {
        var format = scope.Culture.DateTimeFormat;
        var order = string.Concat(format.ShortDatePattern.Where(c => c is 'M' or 'd' or 'y').Distinct());
        var first = true;

        foreach (var part in order)
        {
            if (!first)
            {
                yield return Span.Key("sep-" + part)[format.DateSeparator];
            }

            first = false;
            yield return Segment(part, disabled);
        }
    }

    // A part is the browser's own: no value and no handler, so a render never writes over what is being typed.
    private static HTMLInputElement<string> Segment(char part, bool disabled)
    {
        var (name, kind, placeholder) = part switch
        {
            'M' => ("Month", "month", "mm"),
            'd' => ("Day", "day", "dd"),
            _ => ("Year", "year", "yyyy"),
        };

        return Input.Of<string>()
            .Key(part)
            .Type(InputType.Text)
            .Class(part == 'y' ? UiDatePickerLook.SegmentYear : UiDatePickerLook.Segment)
            .Data(new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-" + kind + "-input"] = "", ["rask-segment"] = kind })
            .Disabled(disabled)
            .Placeholder(placeholder)
            .Aria("label", name)
            .Attributes(("inputmode", "numeric"));
    }

    // The one field the parts are read and written through: the whole date, or nothing.
    private static HTMLInputElement<string> Whole(UiDatePickerScope scope, int slot) =>
        Input
            .Value(scope.Dates[slot]?.ToString(Iso, CultureInfo.InvariantCulture) ?? string.Empty)
            .OnChange(text => DateOnly.TryParseExact(text, Iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? scope.Typed(slot, day)
                : Task.CompletedTask)
            .Key("whole")
            .Type(InputType.Hidden);

    // What Flux's trigger opens the calendar from: everything in it that is not a field. A second press closes it.
    private static Component Opener(UiDatePickerScope scope, bool locked, Ui.IconName icon, string classes)
    {
        if (locked)
        {
            return Ui.Icon.Name(icon).Mini.Class(classes);
        }

        var marks = UiIcon.MarksWith("data-rask-toggle");
        marks["data-rask-toggle"] = scope.PopoverId;
        return UiIcon.Marked(marks, icon, Ui.IconVariant.Mini, classes);
    }
}
