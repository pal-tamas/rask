namespace Rask.UiTests.Components;

/// <summary>
///     The pieces the operator console is drawn with: its content column, page headings, rows of figures,
///     code blocks, empty states and wrapping badges.
/// </summary>
/// <remarks>
///     Each of these used to be spelled out as a class string in <c>Rask.Dashboard</c>, where the kit's
///     compiled sheet could not see it. What is asserted here is that the kit component now carries the
///     layout the console needs, so a page drawn with it writes no class at all.
/// </remarks>
public partial class UiConsoleChromeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Two_figures_across_stay_two_across_at_every_width()
    {
        var html = Ui.MetricRow.Columns(2)[
            Ui.Metric.Label("Outstanding").Value("12"),
            Ui.Metric.Label("Failed").Value("0")
        ].ToHtml();

        Assert.Contains("grid-cols-2", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sm:grid-cols-4", html, StringComparison.Ordinal);
        Assert.DoesNotContain("col-span-2", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_figure_that_reports_a_problem_is_written_in_the_readable_ink()
    {
        var failed = Ui.Metric.Label("Failed").Value("3").Tone(Ui.Tone.Error).ToHtml();
        var unproven = Ui.DetailRow.Label("Backup").Value("inconclusive").Tone(Ui.Tone.Warning).ToHtml();
        var stat = Ui.Stat.Label("Failed").Value("3").Tone(Ui.Tone.Error).ToHtml();

        Assert.Contains("text-ui-danger-ink", failed, StringComparison.Ordinal);
        Assert.Contains("text-ui-warn-ink", unproven, StringComparison.Ordinal);
        Assert.Contains("text-ui-danger-ink", stat, StringComparison.Ordinal);
    }

    [Fact]
    public void A_code_block_with_no_label_is_the_block_alone() =>
        Assert.StartsWith("<pre", Ui.Code.Content("{}").ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_label_sits_above_its_block_and_takes_the_blocks_tone()
    {
        var html = Ui.Code.Content("boom").Label("Last error").Tone(Ui.Tone.Error).ToHtml();

        Assert.True(
            html.IndexOf("Last error", StringComparison.Ordinal) < html.IndexOf("<pre", StringComparison.Ordinal));
        Assert.Contains("font-medium text-ui-danger-ink", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_state_gives_the_answer_before_the_reason()
    {
        var html = Ui.Empty.Title("No queue called jobs").Detail("That battery is not registered.").ToHtml();

        Assert.True(
            html.IndexOf("No queue called jobs", StringComparison.Ordinal)
            < html.IndexOf("That battery is not registered.", StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_state_needs_no_detail() =>
        Assert.DoesNotContain("max-w-prose", Ui.Empty.Title("Nothing stored").ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void An_empty_state_draws_no_border_of_its_own() =>
        Assert.DoesNotContain("border", Ui.Empty.Title("Nothing stored").ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void The_kit_sheet_carries_what_wraps_a_long_token_in_a_badge()
    {
        // Flux's badge has no such prop, so the console hands the utilities to Class — from packages whose
        // sources the kit's Tailwind build never reads. UiBadge's remarks name them, which is what emits them.
        var css = UiStylesheet.Css;

        Assert.Contains(".font-mono{", css, StringComparison.Ordinal);
        Assert.Contains(".max-w-full{", css, StringComparison.Ordinal);
        Assert.Contains(".break-all{", css, StringComparison.Ordinal);
        Assert.Contains(".whitespace-normal\\!{", css, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_badge_keeps_its_words_on_one_line() =>
        Assert.DoesNotContain("break-all", Ui.Badge["Live"].ToHtml(), StringComparison.Ordinal);
}
