namespace Rask.Workbooks;

/// <summary>
///     A day as Excel counts it: days since an origin, the time of day the fraction. In the 1900 system day 1 is
///     1 January 1900 and day 60 is the 29 February 1900 that never was (Lotus 1-2-3's leap-year bug, which Excel
///     keeps); in the 1904 system day 0 is 1 January 1904. A serial below 1 is a time of day alone.
/// </summary>
internal static class ExcelSerial
{
    private const decimal MillisecondsADay = 86_400_000m;
    private const decimal Last = 2_958_466m; // 1 January 10000, in either system

    private static readonly DateTime Origin1900 = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly DateTime Origin1904 = new(1904, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>The serial Excel shows as this wall clock (1900 system): a day before March 1900 is one less, for the day that never was.</summary>
    public static double From(DateTime wallClock)
    {
        var days = (wallClock - Origin1900).TotalDays;
        return days is >= 1 and < 61 ? days - 1 : days;
    }

    /// <summary>The wall clock a serial stands for, to the millisecond; none when it is out of the range a date can hold.</summary>
    public static DateTime? ToWallClock(decimal serial, bool date1904)
    {
        if (serial < 0 || serial >= Last)
        {
            return null;
        }

        var origin = date1904 ? Origin1904 : Origin1900;
        var days = !date1904 && serial is >= 1 and < 60 ? serial + 1 : serial;
        var milliseconds = decimal.Round(days * MillisecondsADay, MidpointRounding.AwayFromZero);
        return milliseconds <= (decimal)(DateTime.MaxValue - origin).TotalMilliseconds - 1
            ? origin.AddMilliseconds((double)milliseconds)
            : null;
    }
}
