namespace Rask.Ui.Tests.Components;

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
    public void The_content_column_spaces_its_sections() =>
        Assert.Contains("flex flex-col gap-4", UiMain[Span["a"], Span["b"]].ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_page_heading_leaves_the_spacing_to_the_column_it_sits_in() =>
        Assert.DoesNotContain("mb-", UiHeader.Heading("Jobs").ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void Two_figures_across_stay_two_across_at_every_width()
    {
        var html = UiMetricRow.Columns(2)[
            UiMetric.Label("Outstanding").Value("12"),
            UiMetric.Label("Failed").Value("0")
        ].ToHtml();

        Assert.Contains("grid-cols-2", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sm:grid-cols-4", html, StringComparison.Ordinal);
        Assert.DoesNotContain("col-span-2", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_code_block_with_no_label_is_the_block_alone() =>
        Assert.StartsWith("<pre", UiCode.Content("{}").ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_label_sits_above_its_block_and_takes_the_blocks_tone()
    {
        var html = UiCode.Content("boom").Label("Last error").Tone(UiTone.Error).ToHtml();

        Assert.True(
            html.IndexOf("Last error", StringComparison.Ordinal) < html.IndexOf("<pre", StringComparison.Ordinal));
        Assert.Contains("font-medium text-error", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_state_gives_the_answer_before_the_reason()
    {
        var html = UiEmpty.Heading("No queue called jobs").Detail("That battery is not registered.").ToHtml();

        Assert.True(
            html.IndexOf("No queue called jobs", StringComparison.Ordinal)
            < html.IndexOf("That battery is not registered.", StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_state_needs_no_detail() =>
        Assert.DoesNotContain("max-w-prose", UiEmpty.Heading("Nothing stored").ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void An_empty_state_draws_no_border_of_its_own() =>
        Assert.DoesNotContain("border", UiEmpty.Heading("Nothing stored").ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void A_mono_badge_wraps_a_long_token_instead_of_widening_its_row()
    {
        var html = UiBadge.Mono(true)["requestId=0HN8Q2V3R1T0K:00000001"].ToHtml();

        Assert.Contains("font-mono", html, StringComparison.Ordinal);
        Assert.Contains("break-all", html, StringComparison.Ordinal);
        Assert.Contains("h-auto", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_modal_spaces_the_sections_it_holds() =>
        Assert.Contains(
            "space-y-4",
            UiModal.Title("Job #12").Open(true)[Span["details"], Span["payload"]].ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void A_plain_badge_keeps_its_fixed_height() =>
        Assert.DoesNotContain("break-all", UiBadge["Live"].ToHtml(), StringComparison.Ordinal);
}
