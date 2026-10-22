using System.Linq.Expressions;
using Rask.Core;
using Rask.Core.Forms;

namespace Rask.Site.Features.UiDocs;

// The pickers' tables: calendar, date picker, time picker and color picker.
internal static partial class UiApi
{
    private const string Day = "a DateOnly";

    private const string Days = "DateOnly days: [today.AddDays(1)]";

    public static IReadOnlyList<UiApiPart> Calendar { get; } =
    [
        new("Ui.Calendar", typeof(UiCalendar), "flux:calendar",
            "A month of days to pick from. Ui.Calendar picks a day, Ui.Calendar.Multiple several, Ui.Calendar.Range a range.",
        [
            new("Bind", typeof(Expression<Func<DateOnly>>), "The member it reads and writes: () => m.Day, a collection of days, or a UiDateRange. Or Value with OnChange.", "() => what the mode binds"),
            new("Value", typeof(DateOnly), "What it holds, for a page that holds it itself. Values(days) for several.", "a DateOnly, days, or a UiDateRange"),
            new("OnChange", typeof(Callback<DateOnly>), "Runs with the new value when a day is picked.", Picked),
            new("AfterBind", typeof(Callback<DateOnly>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<DateOnly>?), "A rule the value is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as soon as a day is picked, not with the next action."),
            new("Mode", typeof(Ui.CalendarMode?), "What is picked, as a value. The opening step says it; this has to agree with what is bound."),
            new("Multiple", typeof(bool?), "Flux's multiple: the same as Mode(Ui.CalendarMode.Multiple)."),
            new("Min", typeof(DateOnly?), "The earliest day that can be picked. Days before it are disabled.", Day),
            new("Max", typeof(DateOnly?), "The latest day that can be picked.", Day),
            new("Unavailable", typeof(IReadOnlyCollection<DateOnly>), "Days that cannot be picked: holidays, booked days.", Days),
            new("Size", typeof(Ui.CalendarSize?), "How large the day cells are. 44px when unset."),
            new("StartDay", typeof(DayOfWeek?), "The day the week starts on. The locale's own when unset."),
            new("Months", typeof(int?), "How many months are shown side by side. One, or two for a range, when unset."),
            new("MinRange", typeof(int?), "The fewest days a range may hold."),
            new("MaxRange", typeof(int?), "The most days a range may hold."),
            new("OpenTo", typeof(DateOnly?), "The day it opens to while nothing is chosen.", Day),
            new("ForceOpenTo", typeof(bool?), "Opens to OpenTo whatever is chosen."),
            new("Navigation", typeof(bool?), "False hides the month steps."),
            new("Static", typeof(bool?), "A calendar to look at: no day can be picked."),
            new("WeekNumbers", typeof(bool?), "Shows each week's number."),
            new("SelectableHeader", typeof(bool?), "Makes the month and the year in the header selects."),
            new("WithToday", typeof(bool?), "A button that comes back to this month, and picks today once there."),
            new("WithInputs", typeof(bool?), "A field over the months to type the day into, or a range's start and end. None for several days."),
            new("FixedWeeks", typeof(bool?), "Always six weeks, so paging never changes the height."),
            new("Locale", typeof(string), "The locale the names and the week follow: \"ja-JP\". The app's culture when unset."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> DatePicker { get; } =
    [
        new("Ui.DatePicker", typeof(UiDatePicker), "flux:date-picker",
            "A field-shaped button over a calendar in a popup. Ui.DatePicker picks a day, Ui.DatePicker.Range a range.",
        [
            new("Bind", typeof(Expression<Func<DateOnly>>), "The member it reads and writes: () => m.Day, or a UiDateRange. Or Value with OnChange.", "() => a DateOnly, or a UiDateRange"),
            new("Value", typeof(DateOnly), "What it holds, for a page that holds it itself.", "a DateOnly, or a UiDateRange"),
            new("OnChange", typeof(Callback<DateOnly>), "Runs with the new value when it is picked.", Picked),
            new("AfterBind", typeof(Callback<DateOnly>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<DateOnly>?), "A rule the value is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as soon as it is picked, not with the next action."),
            new("Mode", typeof(Ui.DatePickerMode?), "What is picked, as a value. The opening step says it; this has to agree with what is bound."),
            new("Type", typeof(Ui.DatePickerType?), "What opens it: a button showing the choice, or a field to type it into. The button when unset."),
            new("Trigger", typeof(Component), "A trigger of your own: Ui.DatePickerInput fields, or a Ui.DatePickerButton."),
            new("Label", typeof(string), "Draws a field around it, with this label and the error."),
            new("Description", typeof(string), "Help text between the label and the picker."),
            new("DescriptionTrailing", typeof(string), "Help text under the picker."),
            new("Badge", typeof(string), "A word beside the label: \"Optional\"."),
            new("Placeholder", typeof(string), "What the trigger shows while nothing is chosen. By mode when unset."),
            new("Min", typeof(DateOnly?), "The earliest day that can be picked; also where All Time starts.", Day),
            new("Max", typeof(DateOnly?), "The latest day that can be picked.", Day),
            new("Unavailable", typeof(IReadOnlyCollection<DateOnly>), "Days that cannot be picked.", Days),
            new("MinRange", typeof(int?), "The fewest days a range may hold."),
            new("MaxRange", typeof(int?), "The most days a range may hold."),
            new("OpenTo", typeof(DateOnly?), "The day the calendar opens to while nothing is chosen.", Day),
            new("ForceOpenTo", typeof(bool?), "Opens to OpenTo whatever is chosen."),
            new("Months", typeof(int?), "How many months are shown side by side. One, or two for a range, when unset."),
            new("Size", typeof(Ui.DatePickerSize?), "How large the calendar's day cells are."),
            new("StartDay", typeof(DayOfWeek?), "The day the week starts on. The locale's own when unset."),
            new("WeekNumbers", typeof(bool?), "Shows each week's number."),
            new("SelectableHeader", typeof(bool?), "Makes the month and the year in the header selects."),
            new("WithToday", typeof(bool?), "A button that comes back to this month, and picks today once there."),
            new("FixedWeeks", typeof(bool?), "Always six weeks, so paging never changes the height."),
            new("WithInputs", typeof(Ui.DatePickerInputVariant?), "Fields over the months to type into: WithInputs() as parts, WithInputs(Ui.DatePickerInputVariant.Native) as the browser's."),
            new("WithConfirmation", typeof(bool?), "Holds the choice until the reader presses Select date."),
            new("WithPresets", typeof(bool?), "Lists Flux's seven preset ranges beside the calendar."),
            new("Presets", typeof(IReadOnlyList<Ui.DateRangePreset>), "The presets listed, in this order. Setting it lists them.", "Ui.DateRangePreset values: [Ui.DateRangePreset.Today]"),
            new("Clearable", typeof(bool?), "A button in the trigger that empties the picker, while a date is chosen."),
            new("Disabled", typeof(bool?), "A picker that cannot be opened."),
            new("Invalid", typeof(bool?), "Error styling the form did not ask for."),
            new("Locale", typeof(string), "The locale the names, the week and the shown date follow. The app's culture when unset."),
            new("ShowValidation", typeof(bool?), "False leaves the member's message to a Ui.Error placed elsewhere."),
        ]),
        new("Ui.DatePickerInput", typeof(UiDatePickerInput), "flux:date-picker.input",
            "A field in a picker's Trigger to type its date into, which also opens the calendar. Native is the browser's own date field.",
        [
            new("Label", typeof(string), "The label over the field, which wraps it in a field."),
            new("Description", typeof(string), "Help text between the label and the field."),
            new("Variant", typeof(Ui.DatePickerInputVariant?), "Typed as parts, or in the browser's own date field. Parts when unset."),
            new("Size", typeof(Ui.DatePickerInputSize?), "How tall the field is."),
            new("Clearable", typeof(bool?), "A button that empties the picker, while a date is chosen. The picker's own when unset."),
            new("Disabled", typeof(bool?), "A field that cannot be typed into or open the picker."),
            new("Invalid", typeof(bool?), "Draws the field as holding an error."),
        ]),
        new("Ui.DatePickerButton", typeof(UiDatePickerButton), "flux:date-picker.button",
            "The field-shaped button in a picker's Trigger, for what the picker's own steps do not reach.",
        [
            new("Placeholder", typeof(string), "What it shows while nothing is chosen. The picker's own when unset."),
            new("Size", typeof(Ui.DatePickerButtonSize?), "How tall it is."),
            new("Clearable", typeof(bool?), "A button that empties the picker, while a date is chosen. The picker's own when unset."),
            new("Disabled", typeof(bool?), "A button that cannot open the picker."),
            new("Invalid", typeof(bool?), "Draws the button as holding an error."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> TimePicker { get; } =
    [
        new("Ui.TimePicker", typeof(UiTimePicker<TimeOnly?>), "flux:time-picker",
            "A button over a list of times. A TimeOnly? picks one or none, a collection of TimeOnly several.",
        [
            new("Bind", typeof(Expression<Func<TimeOnly?>>), "The member it reads and writes: () => m.StartsAt. Or Value with OnChange.", "() => a T"),
            new("Value", typeof(TimeOnly?), "What it holds, for a page that holds it itself.", "T: a TimeOnly?, a TimeOnly, or times"),
            new("OnChange", typeof(Callback<TimeOnly?>), "Runs with the new value when a time is picked.", Picked),
            new("AfterBind", typeof(Callback<TimeOnly?>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<TimeOnly?>?), "A rule the value is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as soon as a time is picked, not with the next action."),
            new("Type", typeof(Ui.TimePickerType?), "A button, or fields to type the time into. The button when unset."),
            new("Dropdown", typeof(bool?), "False leaves the typed trigger without its list."),
            new("TimeFormat", typeof(Ui.TimePickerTimeFormat?), "12-hour or 24-hour, each a step too (.TwelveHour). The culture's own when unset."),
            new("Interval", typeof(int?), "Minutes between the times on offer. 30 when unset."),
            new("Min", typeof(TimeOnly?), "The earliest time on offer.", "a TimeOnly"),
            new("Max", typeof(TimeOnly?), "The latest time on offer.", "a TimeOnly"),
            new("Unavailable", typeof(IReadOnlyList<UiTimeRange>), "Times, and stretches of time, listed and disabled.", "TimeOnly values and UiTimeRange stretches"),
            new("OpenTo", typeof(TimeOnly?), "Where the list opens while nothing is chosen.", "a TimeOnly"),
            new("Label", typeof(string), "Draws a field around it, with this label and the error."),
            new("Description", typeof(string), "Help text between the label and the picker."),
            new("DescriptionTrailing", typeof(string), "Help text under the picker."),
            new("Badge", typeof(string), "A word beside the label: \"Optional\"."),
            new("Placeholder", typeof(string), "What the button shows while no time is chosen. \"Select a time\" when unset."),
            new("Size", typeof(Ui.TimePickerSize?), "How tall it is. 40px when unset."),
            new("Clearable", typeof(bool?), "A button that empties the picker, while a time is chosen."),
            new("Disabled", typeof(bool?), "Takes no input and is skipped by the keyboard."),
            new("Invalid", typeof(bool?), "Error styling the form did not ask for."),
            new("Locale", typeof(string), "The culture the times are written in: \"ja-JP\". The app's culture when unset."),
            new("ShowValidation", typeof(bool?), "False leaves the member's message to a Ui.Error placed elsewhere."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> ColorPicker { get; } =
    [
        new("Ui.ColorPicker", typeof(UiColorPicker), "flux:color-picker",
            "A colour from a palette, an area and sliders, or typed. Its children, when it has any, compose the popover.",
        [
            new("Bind", typeof(Expression<Func<string>>), "The member it reads and writes: () => m.Theme. Or Value with OnChange.", "() => a string"),
            new("Value", typeof(string), "The colour as any CSS colour string, for a page that holds it itself."),
            new("OnChange", typeof(Callback<string>), "Runs with the new colour, in Format.", Picked),
            new("AfterBind", typeof(Callback<string>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<string>?), "A rule the colour is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as soon as a colour is chosen, not with the next action."),
            new("Format", typeof(Ui.ColorPickerFormat?), "What the colour is written as. An alpha format adds an alpha slider. Hex when unset."),
            new("Type", typeof(Ui.ColorPickerType?), "An input with a swatch button, or one button showing the colour. The input when unset."),
            new("Swatches", typeof(IReadOnlyList<UiColorSwatch>), "The palette in place of the 24 Tailwind colours. An empty list shows none.", "hex strings, or (value, label) pairs"),
            new("Dropper", typeof(bool?), "A button in the popover that picks a colour from the screen, where the browser has one."),
            new("Clearable", typeof(bool?), "A button on the input that clears the colour, while there is one."),
            new("Copyable", typeof(bool?), "A button on the input that copies the colour as it is written."),
            new("Placeholder", typeof(string), "Shown while there is no colour. By format when unset: #000000 for hex."),
            new("Size", typeof(Ui.ColorPickerSize?), "How tall the trigger is. 40px when unset."),
            new("Label", typeof(string), "Draws a field around it, with this label and the error."),
            new("Description", typeof(string), "Help text between the label and the picker."),
            new("Name", typeof(string), "Adds a hidden input of this name, so a plain form posts the colour."),
            new("Disabled", typeof(bool?), "A picker that cannot be opened or changed."),
            new("Invalid", typeof(bool?), "Error styling the form did not ask for."),
        ]),
        new("Ui.ColorPickerArea", typeof(UiColorPickerArea), "flux:color-picker.area", "The saturation and brightness area of a composed popover.", []),
        new("Ui.ColorPickerSlider", typeof(UiColorPickerSlider), "flux:color-picker.slider", "The hue or the opacity slider of a composed popover.",
        [
            new("Channel", typeof(Ui.ColorPickerChannel?), "What the slider moves. The hue when unset."),
        ]),
        new("Ui.ColorPickerInput", typeof(UiColorPickerInput), "flux:color-picker.input", "The text input of a composed popover.",
        [
            new("Placeholder", typeof(string), "Shown while there is no colour. The picker's own when unset."),
        ]),
        new("Ui.ColorPickerSwatches", typeof(UiColorPickerSwatches), "flux:color-picker.swatches",
            "The palette of a composed popover: the picker's own, or the Ui.ColorPickerSwatch children it is given.", []),
        new("Ui.ColorPickerSwatch", typeof(UiColorPickerSwatch), "flux:color-picker.swatch", "One colour of a palette.",
        [
            new("Value", typeof(string), "The colour, as a CSS colour string: #ef4444."),
            new("Label", typeof(string), "What assistive tech calls it, and its tooltip. The value when unset."),
        ]),
        new("Ui.ColorPickerDropper", typeof(UiColorPickerDropper), "flux:color-picker.dropper",
            "The dropper button of a composed popover, there only where the browser has an EyeDropper.", []),
    ];
}
