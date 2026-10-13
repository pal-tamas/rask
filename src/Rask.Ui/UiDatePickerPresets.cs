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

    internal static global::Rask.Core.Component Render(
        IReadOnlyList<Ui.DateRangePreset> listed,
        UiDateRange current,
        UiCalendarOptions options,
        string? closes,
        Func<UiDateRange, Task> choose)
    {
        var (rows, ranges, named) = Listed(listed, current, options);

        return Div.Class(UiDatePickerLook.Presets)[
            Div.Class(UiDatePickerLook.PresetList).Role("radiogroup").Data("rask-roving", "")[
                rows.Select((preset, at) => Row(
                    preset, ranges[preset], preset == named, named == Ui.DateRangePreset.Custom ? at == 0 : preset == named, closes, choose))
            ]
        ];
    }

    /// <summary>The same presets as the phone's select: the one that names the range is the chosen option.</summary>
    internal static global::Rask.Core.Component Narrow(
        IReadOnlyList<Ui.DateRangePreset> listed, UiDateRange current, UiCalendarOptions options, Func<UiDateRange, Task> choose)
    {
        var (rows, ranges, named) = Listed(listed, current, options);
        var byKey = rows.ToDictionary(UiDateRangePresets.Key, preset => preset, StringComparer.Ordinal);

        return Div.Class(UiDatePickerLook.PresetSelect)[
            Select
                .Value(UiDateRangePresets.Key(named))
                // Custom names what the calendar picks; it has no range of its own to write.
                .OnChange(key => byKey.TryGetValue(key, out var preset) && preset != Ui.DateRangePreset.Custom
                    ? choose(ranges[preset])
                    : Task.CompletedTask)
                .Class(UiDatePickerLook.PresetSelectControl)
                .Data(SelectMarks)[
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

    private static global::Rask.Core.Component Row(
        Ui.DateRangePreset preset, UiDateRange range, bool on, bool stop, string? closes, Func<UiDateRange, Task> choose)
    {
        // Custom names what the calendar picks; it has no range of its own to write.
        var picks = preset != Ui.DateRangePreset.Custom;
        var hides = picks ? closes : null;
        var row = Button
            .Key(preset)
            .Type(ButtonType.Button)
            .Class(on ? UiDatePickerLook.PresetChecked : UiDatePickerLook.Preset)
            .Role("radio")
            .TabIndex(stop ? 0 : -1)
            .Aria("checked", on ? "true" : "false")
            .Attributes(hides is null
                ? [("value", UiDateRangePresets.Key(preset))]
                : [("value", UiDateRangePresets.Key(preset)), ("popovertarget", hides), ("popovertargetaction", "hide")]);

        // The tab stop is where the arrows start from: Flux marks it, checked or not.
        if (on)
        {
            row = row.Data(stop ? CheckedStop : Checked);
        }
        else if (stop)
        {
            row = row.Data(Stop);
        }

        if (picks)
        {
            row = row.OnClick(() => choose(range));
        }

        return row[UiDateRangePresets.Label(preset)];
    }
}
