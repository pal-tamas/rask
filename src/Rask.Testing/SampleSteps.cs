using System.Globalization;

namespace Rask.Testing;

/// <summary>
///     The ordered types a sample is built of, each with where it starts and its step: 1 and one for a number, today and
///     a day for a date, eight o'clock and an hour for a time.
/// </summary>
internal static class SampleSteps
{
    /// <summary>The first sample of <paramref name="type" />, or null for a type that is no number, date or time.</summary>
    public static IComparable? First(Type type)
    {
        if (IsNumber(type))
        {
            return (IComparable)Convert.ChangeType(1, type, CultureInfo.InvariantCulture);
        }

        return type switch
        {
            _ when type == typeof(DateOnly) => Clock.Today,
            _ when type == typeof(DateTime) => Clock.Today.ToDateTime(TimeOnly.MinValue),
            _ when type == typeof(DateTimeOffset) => Clock.Now,
            _ when type == typeof(TimeOnly) => new TimeOnly(8, 0),
            _ when type == typeof(TimeSpan) => TimeSpan.FromHours(8),
            _ => null,
        };
    }

    /// <summary><paramref name="value" /> one step on, or back with <paramref name="steps" /> of -1.</summary>
    public static IComparable Step(IComparable value, int steps = 1) =>
        value switch
        {
            DateOnly day => day.AddDays(steps),
            DateTime at => at.AddDays(steps),
            DateTimeOffset at => at.AddDays(steps),
            TimeOnly time => time.AddHours(steps),
            TimeSpan span => span + TimeSpan.FromHours(steps),
            _ => (IComparable)Convert.ChangeType(Convert.ToDecimal(value, CultureInfo.InvariantCulture) + steps, value.GetType(), CultureInfo.InvariantCulture),
        };

    private static bool IsNumber(Type type) =>
        Type.GetTypeCode(type) is >= TypeCode.SByte and <= TypeCode.Decimal;
}
