namespace Rask;

/// <summary>The list of preset ranges a range date picker draws before its months — Flux's radio group.</summary>
internal abstract partial class UiDatePickerPresets : global::Rask.Core.RaskMarkup
{
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
        // Flux ends every list with Custom, which is what a range picked by hand — or no range yet — is.
        var rows = listed.Contains(Ui.DateRangePreset.Custom) ? listed : [.. listed, Ui.DateRangePreset.Custom];
        var ranges = rows.ToDictionary(preset => preset, preset => UiDateRange.Of(preset, options.Today, options.StartDay, options.Min));
        var named = rows.FirstOrDefault(
            preset => preset != Ui.DateRangePreset.Custom
                      && current != default
                      && (current.Preset == preset || (current.Preset is null && Same(ranges[preset], current))),
            Ui.DateRangePreset.Custom);

        return Div.Class(UiDatePickerLook.Presets)[
            Div.Class(UiDatePickerLook.PresetList).Role("radiogroup").Data("rask-roving", "")[
                rows.Select((preset, at) => Row(
                    preset, ranges[preset], preset == named, named == Ui.DateRangePreset.Custom ? at == 0 : preset == named, closes, choose))
            ]
        ];
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
