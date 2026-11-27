using System.Net;
using System.Text.RegularExpressions;
using Rask.Background;
using Rask.Core;
using Rask.Core.Messaging;

namespace Rask.UiTests.Components;

/// <summary>
///     What <c>Ui.WorkNotices</c> and <c>Ui.WorkList</c> draw of #1368: a warned job's amber badge, the words its handler said
///     under its title coloured by how it ended, a batch as one group with one bar and one toast for its starter, and the
///     bell's footer.
/// </summary>
public sealed partial class UiWorkBatchTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateTime Before = DateTime.UtcNow.AddHours(-1);
    private static readonly Guid Press = Guid.Parse("01937f2a-0000-7000-8000-00000000aaaa");
    private static readonly Guid OtherPress = Guid.Parse("01937f2a-0000-7000-8000-00000000bbbb");

    [Fact]
    public void A_warned_job_shows_an_amber_warning_badge_and_its_words_in_amber()
    {
        var list = Ui.WorkList.Showing([Work(1, "Import of prices.csv", WorkState.Warned, detail: "No new rows")]);

        var html = list.ToHtml();

        Assert.Contains("data-ui-work=\"warned\"", html, StringComparison.Ordinal);
        Assert.Matches("bg-amber-400/25[^>]*data-ui-badge[^>]*>.*Warning", html);
        Assert.Matches("text-amber-700[^>]*data-ui-work-detail[^>]*>No new rows<", html);
    }

    [Fact]
    public void A_refused_job_shows_failed_with_its_words_in_red_and_a_done_one_its_note_in_grey()
    {
        var list = Ui.WorkList.Showing(
        [
            Work(1, "Import of a.csv", WorkState.Failed, detail: "a.csv has no header row"),
            Work(2, "Import of b.csv", WorkState.Done, detail: "1,200 rows kept"),
        ]);

        var html = list.ToHtml();

        Assert.Matches("text-red-700[^>]*data-ui-work-detail[^>]*>a.csv has no header row<", html);
        Assert.Matches("text-zinc-500[^>]*data-ui-work-detail[^>]*>1,200 rows kept<", html);
    }

    [Fact]
    public void A_running_job_shows_no_detail_until_it_ends()
    {
        var list = Ui.WorkList.Showing([Work(1, "Import", WorkState.Running, detail: "not yet")]);

        var html = list.ToHtml();

        Assert.DoesNotContain("data-ui-work-detail", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_batch_draws_as_one_group_with_its_count_its_worst_end_and_each_job_under_it()
    {
        var list = Ui.WorkList.Showing(
        [
            Work(1, "Import of a.csv", WorkState.Done, batch: Press),
            Work(2, "Import of b.csv", WorkState.Warned, batch: Press, detail: "No new rows"),
            Work(3, "Import of c.csv", WorkState.Done, batch: Press),
            Work(4, "Monthly report", WorkState.Done),
        ]);

        var html = WebUtility.HtmlDecode(list.ToHtml());

        Assert.Equal(1, Regex.Count(html, "data-ui-work-batch=\""));
        Assert.Contains("data-ui-work-batch=\"warned\"", html, StringComparison.Ordinal);
        Assert.Contains(" · 3 items", html, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Count(html, "data-ui-work-item=\""));
        Assert.Contains("Import of b.csv", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-work=\"done\"", html, StringComparison.Ordinal); // the monthly report, on its own
    }

    [Fact]
    public void A_running_batch_draws_one_bar_for_all_of_it()
    {
        var list = Ui.WorkList.Showing(
        [
            Work(1, "Import of a.csv", WorkState.Done, batch: Press, done: 10, of: 10),
            Work(2, "Import of b.csv", WorkState.Running, batch: Press, done: 5, of: 10),
            Work(3, "Import of c.csv", WorkState.Running, batch: Press, done: 0, of: 20),
        ]);

        var html = list.ToHtml();

        Assert.Equal(1, Regex.Count(html, "role=\"progressbar\""));
        Assert.Contains("aria-valuenow=\"15\" aria-valuemax=\"40\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-work-batch=\"running\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Ended_batches_count_once_toward_the_ten_ended_pieces_kept()
    {
        var batch = Enumerable.Range(1, 15).Select(i => Work(i, $"Item {i}", WorkState.Done, batch: Press, at: Before.AddMinutes(30)));
        var singles = Enumerable.Range(100, 12).Select(i => Work(i, $"Single {i}", WorkState.Done, at: Before.AddMinutes(i - 100)));

        var shown = UiWorkFeed.Merge([.. batch, .. singles], []);

        Assert.Equal(15, shown.Count(w => w.Batch == Press));
        Assert.Equal(9, shown.Count(w => w.Batch is null));
    }

    [Fact]
    public void The_bell_toasts_once_when_the_last_job_of_its_users_batch_ends_in_the_tone_of_its_worst()
    {
        using var toasts = Toast.Fake();
        var bell = Ui.WorkNotices.Showing(
            [Work(1, "a", WorkState.Done, batch: Press, mine: true), Work(2, "b", WorkState.Running, batch: Press, mine: true)]);
        bell.ToHtml();
        bell.Showing([Work(1, "a", WorkState.Done, batch: Press, mine: true), Work(2, "b", WorkState.Warned, batch: Press, mine: true)]);

        bell.ToHtml();
        bell.ToHtml();

        toasts.Shown("2 items are done, some with a warning.").Once();
        toasts.Shown("2 items are done, some with a warning.").As(ToastLevel.Warning);
    }

    [Fact]
    public void A_colleagues_shared_batch_and_one_that_ended_before_it_was_seen_raise_no_toast()
    {
        using var toasts = Toast.Fake();
        var bell = Ui.WorkNotices.Showing(
        [
            Work(1, "a", WorkState.Running, batch: Press, mine: false),
            Work(2, "b", WorkState.Done, batch: OtherPress, mine: true),
        ]);
        bell.ToHtml();
        bell.Showing([Work(1, "a", WorkState.Done, batch: Press, mine: false), Work(2, "b", WorkState.Done, batch: OtherPress, mine: true)]);

        bell.ToHtml();

        toasts.Shown("All 1 items are done.").Never();
    }

    [Fact]
    public void A_list_in_the_page_says_a_batchs_end_but_raises_no_toast()
    {
        using var toasts = Toast.Fake();
        var list = Ui.WorkList.Showing([Work(1, "a", WorkState.Running, batch: Press, mine: true)]);
        list.ToHtml();
        list.Showing([Work(1, "a", WorkState.Done, batch: Press, mine: true)]);

        var html = list.ToHtml();

        Assert.Contains("a is done.", html, StringComparison.Ordinal);
        toasts.Shown("All 1 items are done.").Never();
    }

    [Fact]
    public void The_bells_actions_stand_under_its_list_and_none_draws_no_footer()
    {
        var with = Ui.WorkNotices.Actions(A.Href("/loads")["All loads"]).Showing([]);
        var without = Ui.WorkNotices.Showing([]);

        var html = with.ToHtml();

        Assert.Matches("data-ui-work-none[\\s\\S]*data-ui-work-actions[^>]*><a href=\"/loads\">All loads</a>", html);
        Assert.DoesNotContain("data-ui-work-actions", without.ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_reader_of_hungarian_reads_a_batch_and_its_warning_in_hungarian()
    {
        var list = Ui.WorkList.Showing(
            [Work(1, "a", WorkState.Warned, batch: Press), Work(2, "b", WorkState.Done, batch: Press)]);

        var html = WebUtility.HtmlDecode(KitCulture.In("hu-HU", list.ToHtml));

        Assert.Contains(" · 2 tétel", html, StringComparison.Ordinal);
        Assert.Contains("Figyelmeztetés", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shared_job_heard_live_keeps_whose_it_is_from_the_boards_read()
    {
        var row = Work(1, "Load", WorkState.Running, batch: Press, mine: true) with { Shared = true };
        var heard = Work(1, "Load", WorkState.Done, batch: Press) with { Shared = true, At = DateTime.UtcNow };

        var shown = UiWorkFeed.Merge([row], [heard]);

        Assert.True(Assert.Single(shown).Mine);
    }

    private static WorkReported Work(
        int id, string title, WorkState state, Guid? batch = null, string? detail = null, int? done = null, int? of = null,
        DateTime? at = null, bool? mine = null) =>
        new(Id(id), title, null, done, of, state, at ?? Before) { Batch = batch, Detail = detail, Mine = mine };

    // A job's id as a test names it: the nth, a version-7 Guid that reads as its number.
    private static Guid Id(int n) => new($"019ac4f0-5a00-7000-8000-{n:D12}");
}
