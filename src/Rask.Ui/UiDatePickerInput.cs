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
/// Three numeric fields in the locale's own order. A whole, valid date is written to the model as soon as the
/// third field has one; the calendar shows it the next time it opens.
/// </para>
/// </remarks>
public sealed partial class UiDatePickerInput : Component, IUiFieldControl
{
    private static readonly Dictionary<string, string?> Marks = new(StringComparer.Ordinal)
    {
        ["ui-control"] = "",
        ["ui-group-target"] = "",
    };

    private static readonly Dictionary<string, string?> MarksInvalid = new(Marks, StringComparer.Ordinal) { ["invalid"] = "" };

    private static readonly UiPartMarker Inputs = new("ui-date-inputs");

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

    string IUiFieldControl.ControlId => UiFieldId.Derive(Id, null, Label);

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

        var group = Div
            .Id(owns ? field.ControlId : scope.ControlId)
            .Class(UiClass.Compose(UiDatePickerLook.Typed, UiDatePickerLook.Height((int)(Size ?? Ui.DatePickerInputSize.Base)), Class))
            .Data(field.Invalid ? MarksInvalid : Marks)
            .Role("group")
            .Aria(aria)
            .OnClick(() => scope.Dialog.ShowPopover())[
            Ui.Icon.Name(Ui.IconName.Calendar).Mini.Class(UiDatePickerLook.Leading),
            Div.Class(UiDatePickerLook.Segments).Data(Inputs.With(null)).Attributes(("dir", "ltr"))[
                Segments(scope, slot, Disabled == true || scope.Disabled)
            ],
            Span.Class(UiDatePickerLook.Spacer),
            Ui.Icon.Name(Ui.IconName.ChevronDown).Mini.Class(UiDatePickerLook.Trailing)
        ];

        return field.Wrap(group);
    }

    // The locale's own order and separator: mm/dd/yyyy in the US, dd.mm.yyyy in Germany, yyyy/mm/dd in Japan.
    private static IEnumerable<Component> Segments(UiDatePickerScope scope, int slot, bool disabled)
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
            yield return Segment(scope, slot, part, disabled);
        }
    }

    private static HTMLInputElement<string> Segment(UiDatePickerScope scope, int slot, char part, bool disabled)
    {
        var (name, mark, placeholder) = part switch
        {
            'M' => ("Month", "ui-month-input", "mm"),
            'd' => ("Day", "ui-day-input", "dd"),
            _ => ("Year", "ui-year-input", "yyyy"),
        };

        return Input
            .Value(UiDatePickerTyping.Text(scope, slot, part))
            .OnChange(text => UiDatePickerTyping.TypeAsync(scope, slot, part, text))
            .Key(part)
            .Type(InputType.Text)
            .Class(part == 'y' ? UiDatePickerLook.SegmentYear : UiDatePickerLook.Segment)
            .Data(mark, "")
            .Disabled(disabled)
            .Placeholder(placeholder)
            .Aria("label", name)
            .Attributes(("inputmode", "numeric"));
    }
}
