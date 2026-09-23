namespace Rask.Jobs.Tests;

/// <summary>
/// The calendar schedules, tested through the tick the processor claims: <c>Due</c> is the watermark a
/// claim must beat and <c>Next</c> is what the winning claim stamps, so a run happens exactly when
/// <c>LastEnqueuedAt</c> is null or at most <c>Due</c>.
/// </summary>
public sealed class ScheduleTests
{
    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");

    private static Schedule Of(Action<RecurringJob> cadence)
    {
        var options = new JobsOptions();
        var job = options.Run(() => new TickJob());
        cadence(job);
        return job.Schedule!;
    }

    [Fact]
    public void A_daily_job_is_due_once_the_days_hour_has_passed()
    {
        var schedule = Of(j => j.Daily.At(3, 00));

        var (due, next) = schedule.Tick(last: null, now: new DateTime(2026, 6, 10, 4, 00, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);

        Assert.Equal(new DateTime(2026, 6, 10, 3, 00, 0, DateTimeKind.Utc), next);
        Assert.True(due < next, "the watermark must sit before the occurrence, or the tick re-arms itself");
    }

    [Fact]
    public void A_daily_job_asked_before_its_hour_is_still_owed_yesterdays_run()
    {
        var schedule = Of(j => j.Daily.At(3, 00));

        var (_, next) = schedule.Tick(last: null, now: new DateTime(2026, 6, 10, 1, 00, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);

        Assert.Equal(new DateTime(2026, 6, 9, 3, 00, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void A_daily_job_that_already_ran_today_is_not_owed_another()
    {
        var schedule = Of(j => j.Daily.At(3, 00));
        var now = new DateTime(2026, 6, 10, 9, 00, 0, DateTimeKind.Utc);

        var (due, next) = schedule.Tick(last: null, now, TimeZoneInfo.Utc);

        // The processor claims with `LastEnqueuedAt <= due`; stamped with `next`, the same day no longer wins.
        Assert.False(next <= due);
    }

    [Fact]
    public void A_daily_job_reads_its_hour_in_the_apps_time_zone()
    {
        var schedule = Of(j => j.Daily.At(3, 00));

        // 03:00 in Budapest is 01:00 UTC in June (CEST, UTC+2).
        var (_, next) = schedule.Tick(last: null, now: new DateTime(2026, 6, 10, 12, 00, 0, DateTimeKind.Utc), Budapest);

        Assert.Equal(new DateTime(2026, 6, 10, 1, 00, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void A_weekly_job_falls_on_the_last_such_weekday()
    {
        var schedule = Of(j => j.Weekly.On(DayOfWeek.Monday).At(9, 00));

        // Wednesday 10 June 2026 — the Monday before it is the 8th.
        var (_, next) = schedule.Tick(last: null, now: new DateTime(2026, 6, 10, 12, 00, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);

        Assert.Equal(new DateTime(2026, 6, 8, 9, 00, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void A_weekly_job_asked_on_its_day_before_its_hour_falls_a_week_back()
    {
        var schedule = Of(j => j.Weekly.On(DayOfWeek.Monday).At(9, 00));

        // Monday 8 June 2026, 07:00 — this week's run has not come round yet.
        var (_, next) = schedule.Tick(last: null, now: new DateTime(2026, 6, 8, 7, 00, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);

        Assert.Equal(new DateTime(2026, 6, 1, 9, 00, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void A_monthly_job_on_the_thirty_first_runs_on_a_short_months_last_day()
    {
        var schedule = Of(j => j.Monthly.On(31).At(6, 00));

        var (_, next) = schedule.Tick(last: null, now: new DateTime(2026, 2, 28, 12, 00, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);

        Assert.Equal(new DateTime(2026, 2, 28, 6, 00, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void A_monthly_job_asked_before_its_date_falls_into_the_month_before()
    {
        var schedule = Of(j => j.Monthly.On(15).At(6, 00));

        var (_, next) = schedule.Tick(last: null, now: new DateTime(2026, 6, 3, 12, 00, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);

        Assert.Equal(new DateTime(2026, 5, 15, 6, 00, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void An_interval_job_anchors_its_next_run_to_the_last_one()
    {
        var schedule = Of(j => j.Every(TimeSpan.FromHours(1)));
        var last = new DateTime(2026, 6, 10, 9, 00, 0, DateTimeKind.Utc);

        var (_, next) = schedule.Tick(last, now: new DateTime(2026, 6, 10, 10, 05, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);

        // 10:00, not 10:05 — the cadence must not drift by a poll interval each cycle.
        Assert.Equal(new DateTime(2026, 6, 10, 10, 00, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void An_interval_job_that_fell_far_behind_restarts_from_now()
    {
        var schedule = Of(j => j.Every(TimeSpan.FromHours(1)));
        var last = new DateTime(2026, 6, 1, 9, 00, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 6, 10, 10, 05, 0, DateTimeKind.Utc);

        var (_, next) = schedule.Tick(last, now, TimeZoneInfo.Utc);

        // A week of catch-up runs would all be enqueued at once; the app was down, so they are dropped.
        Assert.Equal(now, next);
    }

    [Theory]
    [InlineData(90, "every 1h 30m")]
    [InlineData(300, "every 5h")]
    [InlineData(1440, "every 1d")]
    public void An_interval_reads_back_the_way_an_operator_says_it(int minutes, string expected)
    {
        var schedule = Of(j => j.Every(TimeSpan.FromMinutes(minutes)));

        Assert.Equal(expected, schedule.ToString());
    }

    [Fact]
    public void A_calendar_schedule_reads_back_as_the_sentence_that_built_it()
    {
        Assert.Equal("daily at 03:00", Of(j => j.Daily.At(3, 00)).ToString());
        Assert.Equal("every Monday at 09:00", Of(j => j.Weekly.On(DayOfWeek.Monday).At(9, 00)).ToString());
        Assert.Equal("on day 1 of each month at 06:00", Of(j => j.Monthly.On(1).At(6, 00)).ToString());
    }

    [Fact]
    public void An_hour_outside_the_day_is_refused()
    {
        var options = new JobsOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Run(() => new TickJob()).Daily.At(24, 00));
    }

    [Fact]
    public void A_day_outside_the_month_is_refused()
    {
        var options = new JobsOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Run(() => new TickJob()).Monthly.On(32));
    }

    [Fact]
    public void A_job_scheduled_with_no_cadence_fails_the_hosts_start()
    {
        var options = new JobsOptions();
        options.Run(() => new TickJob());

        // `o.Run<Backup>();` compiles and would then never run — the quietest possible failure.
        var error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(".Daily.At(3, 00)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_schedule_defaults_to_UTC_so_a_deploy_cannot_move_it()
    {
        Assert.Equal(TimeZoneInfo.Utc, new JobsOptions().TimeZone);
    }
}
