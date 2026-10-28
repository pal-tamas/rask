using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

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
/// field opens the calendar from everywhere but its parts (<c>data-rask-toggle</c>): a press in one only puts
/// the caret there.
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

    /// <summary>Typed as parts (custom, unless set) or in the browser's own date field (native).</summary>
    public Ui.DatePickerInputVariant? Variant { get; set; }

    /// <summary>A button that empties the picker, shown while a date is chosen. The picker's own unless set.</summary>
    public bool? Clearable { get; set; }

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

        // The typed date is a plain group, a role with no required state.
        aria.Remove("required");
        if (Label is not null)
        {
            aria["labelledby"] = field.LabelId;
        }

        var locked = Disabled == true || scope.Disabled;
        var height = UiDatePickerLook.Height((int)(Size ?? Ui.DatePickerInputSize.Base));
        var group = Div
            .Id(owns ? field.ControlId : scope.ControlId)
            .Class(UiDatePickerLook.Typed, height, Class)
            .Data(Marked(field.Invalid, locked ? null : scope.PopoverId))
            .Role("group")
            .Aria(aria)[
            Ui.Icon.Name(Ui.IconName.Calendar).Mini.Class(UiDatePickerLook.Leading),
            Variant == Ui.DatePickerInputVariant.Native
                ? Native(scope, slot, locked)
                : Div.Class(UiDatePickerLook.Segments).Data(Inputs).Attributes([("dir", "ltr"), .. scope.FirstEdit])[
                    UiDateParts.Of(scope.Culture, locked),
                    Whole(scope, slot)
                ],
            Span.Class(UiDatePickerLook.Spacer),
            scope.ClearInside(Clearable, locked),
            Ui.Icon.Name(Ui.IconName.ChevronDown).Mini.Class(UiDatePickerLook.TrailingTyped)
        ];

        // Everything in the field but its parts and its × opens the calendar.
        return field.Wrap(group);
    }

    // The one field the parts are read and written through: the whole date, or nothing.
    private static HTMLInputElement<string> Whole(UiDatePickerScope scope, int slot) =>
        Written(scope, slot).Key("whole").Type(InputType.Hidden);

    // The browser's own date field, which says "yyyy-mm-dd" as the parts' field does; the calendar reads and
    // fills it the same way (data-rask-date).
    private static HTMLInputElement<string> Native(UiDatePickerScope scope, int slot, bool disabled) =>
        Written(scope, slot)
            .Key("native")
            .Type(InputType.Date)
            .Class(UiDatePickerLook.Native)
            .Data("rask-date", string.Empty)
            .Disabled(disabled)
            .Attributes(("dir", "ltr"));

    private static HTMLInputElement<string> Written(UiDatePickerScope scope, int slot)
    {
        var field = Input
            .Value(scope.Dates[slot]?.ToString(Iso, CultureInfo.InvariantCulture) ?? string.Empty)
            // No date: the parts were left half filled, which the frame says beside its empty value, or emptied.
            .OnChange(text => DateOnly.TryParseExact(text, Iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? scope.Typed(slot, day)
                : scope.Left(slot, UnreadInput.Current is not null));

        // Bound and waiting, it is the browser's to keep until the next action, which it goes ahead of.
        return scope.Waits ? field.Attributes(UiCalendarHook.WaitsForAction) : field;
    }

    // What Flux's trigger opens the calendar from is everything in it that is not a field, and a second press
    // closes it: the runtime's toggle, which leaves a press in a field to the field.
    private static Dictionary<string, string?> Marked(bool invalid, string? opens)
    {
        var marks = invalid ? MarksInvalid : Marks;
        return opens is null ? marks : new(marks, StringComparer.Ordinal) { ["rask-toggle"] = opens };
    }
}
