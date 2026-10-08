namespace Rask;

/// <summary>What each of Flux UI's range presets means on a given day, and what its row is called.</summary>
internal static class UiDateRangePresets
{
    /// <summary>The list a date picker shows when <c>WithPresets</c> is on and <c>Presets</c> names none.</summary>
    internal static readonly Ui.DateRangePreset[] Default =
    [
        Ui.DateRangePreset.Today, Ui.DateRangePreset.Yesterday, Ui.DateRangePreset.ThisWeek, Ui.DateRangePreset.Last7Days,
        Ui.DateRangePreset.ThisMonth, Ui.DateRangePreset.YearToDate, Ui.DateRangePreset.AllTime,
    ];

    internal static string Label(Ui.DateRangePreset preset) => preset switch
    {
        Ui.DateRangePreset.Today => "Today",
        Ui.DateRangePreset.Yesterday => "Yesterday",
        Ui.DateRangePreset.ThisWeek => "This Week",
        Ui.DateRangePreset.LastWeek => "Last Week",
        Ui.DateRangePreset.Last7Days => "Last 7 Days",
        Ui.DateRangePreset.ThisMonth => "This Month",
        Ui.DateRangePreset.LastMonth => "Last Month",
        Ui.DateRangePreset.ThisQuarter => "This Quarter",
        Ui.DateRangePreset.LastQuarter => "Last Quarter",
        Ui.DateRangePreset.ThisYear => "This Year",
        Ui.DateRangePreset.LastYear => "Last Year",
        Ui.DateRangePreset.Last14Days => "Last 14 Days",
        Ui.DateRangePreset.Last30Days => "Last 30 Days",
        Ui.DateRangePreset.Last3Months => "Last 3 Months",
        Ui.DateRangePreset.Last6Months => "Last 6 Months",
        Ui.DateRangePreset.YearToDate => "Year to Date",
        Ui.DateRangePreset.Tomorrow => "Tomorrow",
        Ui.DateRangePreset.NextWeek => "Next Week",
        Ui.DateRangePreset.Next7Days => "Next 7 Days",
        Ui.DateRangePreset.NextMonth => "Next Month",
        Ui.DateRangePreset.NextQuarter => "Next Quarter",
        Ui.DateRangePreset.NextYear => "Next Year",
        Ui.DateRangePreset.Next14Days => "Next 14 Days",
        Ui.DateRangePreset.Next30Days => "Next 30 Days",
        Ui.DateRangePreset.Next3Months => "Next 3 Months",
        Ui.DateRangePreset.Next6Months => "Next 6 Months",
        Ui.DateRangePreset.AllTime => "All Time",
        _ => "Custom",
    };

    /// <summary>Flux's key for the preset: <c>last7Days</c>, the value its row carries.</summary>
    internal static string Key(Ui.DateRangePreset preset)
    {
        var name = preset.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    internal static UiDateRange Resolve(Ui.DateRangePreset preset, DateOnly today, DayOfWeek startDay, DateOnly? min)
    {
        var week = today.AddDays(-(((int)today.DayOfWeek - (int)startDay + 7) % 7));
        var month = new DateOnly(today.Year, today.Month, 1);
        var quarter = new DateOnly(today.Year, (((today.Month - 1) / 3) * 3) + 1, 1);
        var year = new DateOnly(today.Year, 1, 1);

        return preset switch
        {
            Ui.DateRangePreset.Today => new(today, today),
            Ui.DateRangePreset.Yesterday => new(today.AddDays(-1), today.AddDays(-1)),
            Ui.DateRangePreset.ThisWeek => new(week, week.AddDays(6)),
            Ui.DateRangePreset.LastWeek => new(week.AddDays(-7), week.AddDays(-1)),
            Ui.DateRangePreset.Last7Days => new(today.AddDays(-6), today),
            Ui.DateRangePreset.ThisMonth => new(month, month.AddMonths(1).AddDays(-1)),
            Ui.DateRangePreset.LastMonth => new(month.AddMonths(-1), month.AddDays(-1)),
            Ui.DateRangePreset.ThisQuarter => new(quarter, quarter.AddMonths(3).AddDays(-1)),
            Ui.DateRangePreset.LastQuarter => new(quarter.AddMonths(-3), quarter.AddDays(-1)),
            Ui.DateRangePreset.ThisYear => new(year, year.AddYears(1).AddDays(-1)),
            Ui.DateRangePreset.LastYear => new(year.AddYears(-1), year.AddDays(-1)),
            Ui.DateRangePreset.Last14Days => new(today.AddDays(-13), today),
            Ui.DateRangePreset.Last30Days => new(today.AddDays(-29), today),
            Ui.DateRangePreset.Last3Months => new(today.AddMonths(-3), today),
            Ui.DateRangePreset.Last6Months => new(today.AddMonths(-6), today),
            Ui.DateRangePreset.YearToDate => new(year, today),
            Ui.DateRangePreset.Tomorrow => new(today.AddDays(1), today.AddDays(1)),
            Ui.DateRangePreset.NextWeek => new(week.AddDays(7), week.AddDays(13)),
            Ui.DateRangePreset.Next7Days => new(today, today.AddDays(6)),
            Ui.DateRangePreset.NextMonth => new(month.AddMonths(1), month.AddMonths(2).AddDays(-1)),
            Ui.DateRangePreset.NextQuarter => new(quarter.AddMonths(3), quarter.AddMonths(6).AddDays(-1)),
            Ui.DateRangePreset.NextYear => new(year.AddYears(1), year.AddYears(2).AddDays(-1)),
            Ui.DateRangePreset.Next14Days => new(today, today.AddDays(13)),
            Ui.DateRangePreset.Next30Days => new(today, today.AddDays(29)),
            Ui.DateRangePreset.Next3Months => new(today, today.AddMonths(3)),
            Ui.DateRangePreset.Next6Months => new(today, today.AddMonths(6)),
            Ui.DateRangePreset.AllTime => new(min ?? today, today),
            _ => default,
        };
    }
}
