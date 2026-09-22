namespace Rask;

/// <summary>
///     Durations and sizes written the way they are said: <c>3.Seconds</c>, <c>1.5.Hours</c>,
///     <c>50.Megabytes</c>, <c>3.Days.Ago</c>.
/// </summary>
/// <remarks>
///     <para>
///         A duration is a plain <see cref="TimeSpan" /> and a size a plain <see cref="long" /> byte count, so
///         each goes anywhere the .NET type already does — <c>Task.Delay(3.Seconds)</c>,
///         <c>o.MaxFileSize = 50.Megabytes</c>.
///     </para>
///     <para>
///         Sizes are binary: <c>1.Kilobyte</c> is 1024 bytes and <c>1.Megabyte</c> is 1024 × 1024, the numbers
///         .NET and Windows show. A size that would overflow a <see cref="long" /> throws rather than wrapping.
///     </para>
///     <para>
///         Singular and plural are the same value (<c>1.Hour == 1.Hours</c>); use the one that reads, and the
///         analyzer points out <c>2.Hour</c>.
///     </para>
/// </remarks>
public static class Units
{
    extension(int value)
    {
        /// <summary>This many milliseconds.</summary>
        public TimeSpan Milliseconds => TimeSpan.FromMilliseconds(value);

        /// <summary>One millisecond, for <c>1.Millisecond</c>.</summary>
        public TimeSpan Millisecond => TimeSpan.FromMilliseconds(value);

        /// <summary>This many seconds.</summary>
        public TimeSpan Seconds => TimeSpan.FromSeconds(value);

        /// <summary>One second, for <c>1.Second</c>.</summary>
        public TimeSpan Second => TimeSpan.FromSeconds(value);

        /// <summary>This many minutes.</summary>
        public TimeSpan Minutes => TimeSpan.FromMinutes(value);

        /// <summary>One minute, for <c>1.Minute</c>.</summary>
        public TimeSpan Minute => TimeSpan.FromMinutes(value);

        /// <summary>This many hours.</summary>
        public TimeSpan Hours => TimeSpan.FromHours(value);

        /// <summary>One hour, for <c>1.Hour</c>.</summary>
        public TimeSpan Hour => TimeSpan.FromHours(value);

        /// <summary>This many days of 24 hours.</summary>
        public TimeSpan Days => TimeSpan.FromDays(value);

        /// <summary>One day, for <c>1.Day</c>.</summary>
        public TimeSpan Day => TimeSpan.FromDays(value);

        /// <summary>This many weeks of seven days.</summary>
        public TimeSpan Weeks => TimeSpan.FromDays(checked(value * 7L));

        /// <summary>One week, for <c>1.Week</c>.</summary>
        public TimeSpan Week => TimeSpan.FromDays(checked(value * 7L));

        /// <summary>This many bytes.</summary>
        public long Bytes => value;

        /// <summary>One byte, for <c>1.Byte</c>.</summary>
        public long Byte => value;

        /// <summary>This many kilobytes of 1024 bytes.</summary>
        public long Kilobytes => checked(value * Kibi);

        /// <summary>One kilobyte, for <c>1.Kilobyte</c>.</summary>
        public long Kilobyte => checked(value * Kibi);

        /// <summary>This many megabytes of 1024 × 1024 bytes.</summary>
        public long Megabytes => checked(value * Mebi);

        /// <summary>One megabyte, for <c>1.Megabyte</c>.</summary>
        public long Megabyte => checked(value * Mebi);

        /// <summary>This many gigabytes of 1024³ bytes.</summary>
        public long Gigabytes => checked(value * Gibi);

        /// <summary>One gigabyte, for <c>1.Gigabyte</c>.</summary>
        public long Gigabyte => checked(value * Gibi);
    }

    extension(long value)
    {
        /// <summary>This many bytes.</summary>
        public long Bytes => value;

        /// <summary>This many kilobytes of 1024 bytes.</summary>
        public long Kilobytes => checked(value * Kibi);

        /// <summary>This many megabytes of 1024 × 1024 bytes.</summary>
        public long Megabytes => checked(value * Mebi);

        /// <summary>This many gigabytes of 1024³ bytes.</summary>
        public long Gigabytes => checked(value * Gibi);
    }

    extension(double value)
    {
        /// <summary>This many milliseconds, for <c>0.5.Milliseconds</c>.</summary>
        public TimeSpan Milliseconds => TimeSpan.FromMilliseconds(value);

        /// <summary>This many seconds, for <c>1.5.Seconds</c>.</summary>
        public TimeSpan Seconds => TimeSpan.FromSeconds(value);

        /// <summary>This many minutes, for <c>2.5.Minutes</c>.</summary>
        public TimeSpan Minutes => TimeSpan.FromMinutes(value);

        /// <summary>This many hours, for <c>1.5.Hours</c>.</summary>
        public TimeSpan Hours => TimeSpan.FromHours(value);

        /// <summary>This many days of 24 hours, for <c>0.5.Days</c>.</summary>
        public TimeSpan Days => TimeSpan.FromDays(value);

        /// <summary>This many weeks of seven days, for <c>1.5.Weeks</c>.</summary>
        public TimeSpan Weeks => TimeSpan.FromDays(value * 7);
    }

    extension(TimeSpan span)
    {
        /// <summary>That long before now, on the app's clock: <c>3.Days.Ago</c>.</summary>
        /// <remarks>Reads the same clock as the rest of the app, so a test that freezes time freezes this too.</remarks>
        public DateTimeOffset Ago => AmbientClock.Now - span;

        /// <summary>That long after now, on the app's clock: <c>2.Hours.FromNow</c>.</summary>
        /// <remarks>Reads the same clock as the rest of the app, so a test that freezes time freezes this too.</remarks>
        public DateTimeOffset FromNow => AmbientClock.Now + span;
    }

    private const long Kibi = 1024;
    private const long Mebi = Kibi * 1024;
    private const long Gibi = Mebi * 1024;
}
