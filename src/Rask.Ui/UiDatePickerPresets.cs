namespace Rask;

/// <summary>
///     The preset ranges of a range date picker, as Flux draws them twice: a radio group before the months, and
///     under 640 px a native select below them.
/// </summary>
internal abstract partial class UiDatePickerPresets : global::Rask.Core.RaskMarkup
{
    // Flux's markers on the phone's select, which the stylesheet draws the chevron by.
    private static readonly Dictionary<string, string?> SelectMarks = new(StringComparer.Ordinal)
    {
        ["ui-control"] = "",
        ["ui-select-native"] = "",
        ["ui-group-target"] = "",
    };

    private static readonly Dictionary<string, string?> Checked = new(StringComparer.Ordinal) { ["checked"] = "" };

    private static readonly Dictionary<string, string?> Stop = new(StringComparer.Ordinal) { ["active"] = "" };

    private static readonly Dictionary<string, string?> CheckedStop = new(StringComparer.Ordinal) { ["active"] = "", ["checked"] = "" };

    // A preset is picked in the browser (rask-calendar.ts), which reads its range from the row: the page wrote
    // what each means today.
    private static readonly Dictionary<string, string?> ListMarks = new(StringComparer.Ordinal)
    {
        ["rask-roving"] = "",
        ["rask-calendar-part"] = "presets",
    };

    internal static global::Rask.Core.Component Render(IReadOnlyList<Ui.DateRangePreset> listed, UiDateRange current, UiCalendarOptions options)
    {
        var (rows, ranges, named) = Listed(listed, current, options);

        return Div.Class(UiDatePickerLook.Presets)[
            Div.Class(UiDatePickerLook.PresetList).Role("radiogroup").Data(ListMarks)[
                rows.Select((preset, at) => Row(
                    preset, ranges[preset], preset == named, named == Ui.DateRangePreset.Custom ? at == 0 : preset == named))
            ]
        ];
    }

    /// <summary>The same presets as the phone's select: the one that names the range is the chosen option.</summary>
    internal static global::Rask.Core.Component Narrow(IReadOnlyList<Ui.DateRangePreset> listed, UiDateRange current, UiCalendarOptions options)
    {
        var (rows, _, named) = Listed(listed, current, options);

        return Div.Class(UiDatePickerLook.PresetSelect)[
            Select
                .Value(UiDateRangePresets.Key(named))
                .Class(UiDatePickerLook.PresetSelectControl)
                .Data(SelectMarks)
                .Attributes(UiCalendarHook.Named("preset-select"))[
                Option.Value(string.Empty).Disabled()[RaskStrings.Get(RaskString.DatePickerPresetsPlaceholder, "Choose predefined range...")],
                rows.Select(preset => Option.Key(preset).Value(UiDateRangePresets.Key(preset))[UiDateRangePresets.Label(preset)])
            ]
        ];
    }

    // Flux ends every list with Custom, which is what a range picked by hand — or no range yet — is.
    private static (IReadOnlyList<Ui.DateRangePreset> Rows, Dictionary<Ui.DateRangePreset, UiDateRange> Ranges, Ui.DateRangePreset Named) Listed(
        IReadOnlyList<Ui.DateRangePreset> listed, UiDateRange current, UiCalendarOptions options)
    {
        IReadOnlyList<Ui.DateRangePreset> rows = listed.Contains(Ui.DateRangePreset.Custom) ? listed : [.. listed, Ui.DateRangePreset.Custom];
        var ranges = rows.ToDictionary(preset => preset, preset => UiDateRange.Of(preset, options.Today, options.StartDay, options.Min));
        var named = rows.FirstOrDefault(
            preset => preset != Ui.DateRangePreset.Custom
                      && current != default
                      && (current.Preset == preset || (current.Preset is null && Same(ranges[preset], current))),
            Ui.DateRangePreset.Custom);

        return (rows, ranges, named);
    }

    private static bool Same(UiDateRange a, UiDateRange b) => a.Start == b.Start && a.End == b.End;

    // Custom names what the calendar picks; it has no range of its own to write.
    private static global::Rask.Core.Component Row(Ui.DateRangePreset preset, UiDateRange range, bool on, bool stop)
    {
        var row = Button
            .Key(preset)
            .Type(ButtonType.Button)
            .Class(on ? UiDatePickerLook.PresetChecked : UiDatePickerLook.Preset)
            .Role("radio")
            .TabIndex(stop ? 0 : -1)
            .Aria("checked", on ? "true" : "false")
            .Attributes(preset == Ui.DateRangePreset.Custom
                ? [("value", UiDateRangePresets.Key(preset))]
                : [("value", UiDateRangePresets.Key(preset)), ("data-rask-calendar-range", UiCalendarValue.Range(range with { Preset = null }))]);

        // The tab stop is where the arrows start from: Flux marks it, checked or not.
        if (on)
        {
            row = row.Data(stop ? CheckedStop : Checked);
        }
        else if (stop)
        {
            row = row.Data(Stop);
        }

        return row[UiDateRangePresets.Label(preset)];
    }
}
