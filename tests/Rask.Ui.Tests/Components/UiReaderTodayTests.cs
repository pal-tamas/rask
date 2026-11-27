using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     "Today" is the reader's: a reader in Budapest at half past midnight is on the 20th while the server, on UTC, is
///     still on the 19th. The calendar's today, its presets and the datetime picker's starting time all read it.
/// </summary>
public sealed partial class UiReaderTodayTests : global::Rask.Core.RaskMarkup
{
    // 2026-11-19 23:30 UTC is 2026-11-20 00:30 in Budapest (CET, +1).
    private static readonly DateTimeOffset LateUtc = new(2026, 11, 19, 23, 30, 0, TimeSpan.Zero);

    [Fact]
    public void The_calendar_s_today_is_the_reader_s_day_not_the_server_s()
    {
        using var services = Services();

        var html = Page.Render(() => Ui.Calendar.Value(default(DateOnly)), services).Html;

        Assert.Contains("&quot;today&quot;:&quot;2026-11-20&quot;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_today_preset_counts_from_the_reader_s_day()
    {
        using var services = Services();

        var today = Within(services, UiCalendarOptions.Now);
        var preset = UiDateRange.Of(Ui.DateRangePreset.Yesterday, today);

        Assert.Equal(new DateOnly(2026, 11, 20), today);
        Assert.Equal(new UiDateRange(new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 19)), preset with { Preset = null });
    }

    [Fact]
    public void A_time_picker_starts_from_the_reader_s_wall_clock()
    {
        using var services = Services();

        var now = Within(services, UiCalendarOptions.TimeNow);

        Assert.Equal(new TimeOnly(0, 30), now);
    }

    private static ServiceProvider Services()
    {
        var reader = new Reader(new ManualClock(LateUtc), Reader.ZoneOf("Europe/Budapest"));
        return new ServiceCollection().AddSingleton(reader).BuildServiceProvider();
    }

    private static T Within<T>(IServiceProvider services, Func<T> read)
    {
        var value = default(T);
        Page.Render(() =>
        {
            value = read();
            return Span["x"];
        }, services);
        return value!;
    }
}
