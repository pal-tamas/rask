namespace Rask;

public static partial class Ui
{
    /// <summary>A frequently used range a date picker offers beside its calendar — Flux UI's <c>presets</c>.</summary>
    public enum DateRangePreset
    {
        /// <summary>The current day.</summary>
        Today = 0,

        /// <summary>The previous day.</summary>
        Yesterday,

        /// <summary>The week today is in.</summary>
        ThisWeek,

        /// <summary>The week before this one.</summary>
        LastWeek,

        /// <summary>The seven days ending today.</summary>
        Last7Days,

        /// <summary>The month today is in.</summary>
        ThisMonth,

        /// <summary>The month before this one.</summary>
        LastMonth,

        /// <summary>The quarter today is in.</summary>
        ThisQuarter,

        /// <summary>The quarter before this one.</summary>
        LastQuarter,

        /// <summary>The year today is in.</summary>
        ThisYear,

        /// <summary>The year before this one.</summary>
        LastYear,

        /// <summary>The fourteen days ending today.</summary>
        Last14Days,

        /// <summary>The thirty days ending today.</summary>
        Last30Days,

        /// <summary>The three months ending today.</summary>
        Last3Months,

        /// <summary>The six months ending today.</summary>
        Last6Months,

        /// <summary>January 1st to today.</summary>
        YearToDate,

        /// <summary>The next day.</summary>
        Tomorrow,

        /// <summary>The week after this one.</summary>
        NextWeek,

        /// <summary>The seven days starting today.</summary>
        Next7Days,

        /// <summary>The month after this one.</summary>
        NextMonth,

        /// <summary>The quarter after this one.</summary>
        NextQuarter,

        /// <summary>The year after this one.</summary>
        NextYear,

        /// <summary>The fourteen days starting today.</summary>
        Next14Days,

        /// <summary>The thirty days starting today.</summary>
        Next30Days,

        /// <summary>The three months starting today.</summary>
        Next3Months,

        /// <summary>The six months starting today.</summary>
        Next6Months,

        /// <summary>The picker's <c>Min</c> to today.</summary>
        AllTime,

        /// <summary>A range the reader picked that no other listed preset names.</summary>
        Custom,
    }
}
