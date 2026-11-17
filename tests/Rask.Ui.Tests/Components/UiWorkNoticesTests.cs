using System.Net;
using System.Text.RegularExpressions;
using Rask.Background;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.WorkNotices</c> and <c>Ui.WorkList</c> (#1293): a running job's bar, the badge of each way a job ends, the bell's
///     count of what runs and what ended since it was opened, and the polite announcement when a job seen running ends.
/// </summary>
public sealed partial class UiWorkNoticesTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateTime Before = DateTime.UtcNow.AddHours(-1);
    private static readonly DateTime After = DateTime.UtcNow.AddHours(1);

    [Fact]
    public void A_bell_with_no_work_has_no_count_and_its_flyout_says_there_is_none()
    {
        var bell = Bell();

        var html = bell.ToHtml();

        Assert.Contains("aria-label=\"Background work\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-work-count", html, StringComparison.Ordinal);
        Assert.Contains("No background work.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_running_job_shows_its_bar_filled_to_its_count_with_what_it_said()
    {
        var bell = Bell(Work(1, "Import of prices.csv", WorkState.Running, done: 1200, of: 5000, progress: "1,200 rows kept"));

        var html = bell.ToHtml();

        Assert.Matches("role=\"progressbar\" aria-valuemin=\"0\" aria-valuenow=\"1200\" aria-valuemax=\"5000\" aria-label=\"Import of prices.csv\"", html);
        Assert.Contains("1,200 of 5,000", html, StringComparison.Ordinal);
        Assert.Contains("1,200 rows kept", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-work=\"running\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_running_job_that_gave_no_count_shows_a_bar_without_an_end_and_says_it_runs()
    {
        var bell = Bell(Work(1, "Export", WorkState.Running));

        var html = WebUtility.HtmlDecode(bell.ToHtml());

        Assert.Contains("data-ui-work-unknown", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-valuenow", html, StringComparison.Ordinal);
        Assert.Contains("Running…", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WorkState.Done, "done", "Done")]
    [InlineData(WorkState.Failed, "failed", "Failed")]
    [InlineData(WorkState.Stopped, "stopped", "Stopped")]
    public void An_ended_job_says_how_it_ended_in_a_badge_and_draws_no_bar(WorkState state, string mark, string words)
    {
        var list = List(Work(1, "Import", state, done: 4, of: 4));

        var html = list.ToHtml();

        Assert.Contains($"data-ui-work=\"{mark}\"", html, StringComparison.Ordinal);
        Assert.Matches($"data-ui-badge[^>]*>.*{words}", html);
        Assert.DoesNotContain("progressbar", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_count_is_what_runs_and_what_ended_since_the_bell_was_first_drawn_or_opened()
    {
        var bell = Bell(
            Work(1, "Running", WorkState.Running),
            Work(2, "Ended just now", WorkState.Done, at: After),
            Work(3, "Ended before", WorkState.Failed, at: Before));

        var html = bell.ToHtml();

        Assert.Matches("data-ui-work-count[^>]*>2<", html);
        Assert.Contains("aria-label=\"Background work, 2 new\"", html, StringComparison.Ordinal);
        Assert.Matches("aria-hidden=\"true\"[^>]*data-ui-work-count", html);
    }

    [Fact]
    public void A_job_seen_running_that_ends_is_announced_politely_once()
    {
        var bell = Bell(Work(1, "Import of prices.csv", WorkState.Running, done: 2, of: 4));
        var running = bell.ToHtml();
        bell.Showing([Work(1, "Import of prices.csv", WorkState.Done, done: 4, of: 4, at: After)]);

        var ended = bell.ToHtml();

        Assert.Matches("<div [^>]*role=\"status\" aria-live=\"polite\"[^>]*data-ui-work-said[^>]*></div>", running);
        Assert.Matches("role=\"status\" aria-live=\"polite\"[^>]*data-ui-work-said[^>]*>Import of prices.csv is done.</div>", ended);
    }

    [Fact]
    public void A_job_that_ended_before_it_was_seen_running_is_not_announced()
    {
        var list = List(Work(1, "Old export", WorkState.Failed, at: Before));

        var html = list.ToHtml();

        Assert.Matches("data-ui-work-said[^>]*></div>", html);
    }

    [Fact]
    public void A_reader_of_hungarian_reads_the_bell_and_its_ends_in_hungarian()
    {
        var bell = Bell(Work(1, "Import", WorkState.Running, done: 1, of: 3), Work(2, "Export", WorkState.Stopped, at: Before));

        var html = WebUtility.HtmlDecode(KitCulture.In("hu-HU", bell.ToHtml));

        Assert.Contains("aria-label=\"Háttérfeladatok, 1 új\"", html, StringComparison.Ordinal);
        Assert.Contains("1 / 3", html, StringComparison.Ordinal);
        Assert.Contains("Leállítva", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_latest_word_on_a_job_wins_and_an_end_beats_a_later_running_report()
    {
        var row = Work(1, "Import", WorkState.Running, done: 1, of: 9, at: Before);
        var heard = Work(1, "Import", WorkState.Running, done: 5, of: 9, at: After);
        var endedRow = Work(2, "Export", WorkState.Done, at: Before);
        var lateReport = Work(2, "Export", WorkState.Running, done: 8, of: 9, at: After);

        var shown = UiWorkFeed.Merge([row, endedRow], [heard, lateReport]);

        Assert.Equal(5, shown.Single(w => w.Id == 1).Done);
        Assert.Equal(WorkState.Done, shown.Single(w => w.Id == 2).State);
    }

    [Fact]
    public void Running_work_comes_first_and_only_the_last_ten_ended_are_kept()
    {
        var ended = Enumerable.Range(1, 12).Select(i => Work(i, $"Ended {i}", WorkState.Done, at: Before.AddMinutes(i)));

        var shown = UiWorkFeed.Merge([.. ended, Work(99, "Running", WorkState.Running)], []);

        Assert.Equal(11, shown.Count);
        Assert.Equal(99, shown[0].Id);
        Assert.Equal(12, shown[1].Id);
        Assert.DoesNotContain(shown, w => w.Id is 1 or 2);
    }

    [Fact]
    public void A_list_draws_its_rows_in_the_page_with_its_own_live_region()
    {
        var list = List(Work(1, "Import", WorkState.Running, done: 1, of: 2), Work(2, "Export", WorkState.Done, at: Before));

        var html = list.ToHtml();

        Assert.Equal(2, Regex.Count(html, "data-ui-work=\""));
        Assert.Contains("data-ui-work-said", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-work-bell", html, StringComparison.Ordinal);
    }

    private static UiWorkNotices Bell(params WorkReported[] work) => Ui.WorkNotices.Showing(work);

    private static UiWorkList List(params WorkReported[] work) => Ui.WorkList.Showing(work);

    private static WorkReported Work(
        long id, string title, WorkState state, int? done = null, int? of = null, string? progress = null, DateTime? at = null) =>
        new(id, title, progress, done, of, state, at ?? Before);
}
