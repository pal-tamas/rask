namespace Rask.Background;

/// <summary>Range checks shared by the calendar steps, so each one reads the same way when it is wrong.</summary>
internal static class Schedules
{
    internal static TimeOnly Time(int hour, int minute)
    {
        if (hour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(hour), hour, "An hour of the day is between 0 and 23.");
        }

        if (minute is < 0 or > 59)
        {
            throw new ArgumentOutOfRangeException(nameof(minute), minute, "A minute is between 0 and 59.");
        }

        return new TimeOnly(hour, minute);
    }
}
