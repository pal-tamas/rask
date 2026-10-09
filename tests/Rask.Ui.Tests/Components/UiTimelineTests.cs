namespace Rask.UiTests.Components;

/// <summary>
///     Flux's timeline: the markup each part writes, and what an indicator is drawn as.
/// </summary>
/// <remarks>
///     The track itself — which cell each part sits in, where the lines stop — is the stylesheet's, and is
///     held to Flux's by <c>scripts/flux/parity.mjs timeline</c>. These hold the contract the stylesheet reads.
/// </remarks>
public partial class UiTimelineTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_timeline_is_an_ordered_list_aligned_to_the_centre()
    {
        var timeline = Ui.Timeline[Ui.TimelineItem[Ui.TimelineContent["Shipped"]]];

        var html = timeline.ToHtml();

        Assert.StartsWith("<ol data-ui-timeline data-ui-timeline-align=\"center\">", html, StringComparison.Ordinal);
        Assert.EndsWith("</ol>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_timeline_says_its_direction_size_and_alignment_for_the_stylesheet()
    {
        var timeline = Ui.Timeline.Horizontal().Lg.End;

        var html = timeline.ToHtml();

        Assert.Contains("data-ui-timeline-align=\"end\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-timeline-size=\"lg\"", html, StringComparison.Ordinal);
        Assert.Contains(" data-ui-timeline-horizontal", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_wraps_what_it_holds_in_its_two_lines()
    {
        var item = Ui.TimelineItem[Ui.TimelineContent["Shipped"]];

        var html = item.ToHtml();

        Assert.Equal(
            "<li data-ui-timeline-item>"
            + "<div data-ui-timeline-line-leading><div></div></div><div data-ui-timeline-gap-leading></div>"
            + "<div data-ui-timeline-content>Shipped</div>"
            + "<div data-ui-timeline-line-trailing><div></div></div><div data-ui-timeline-gap-trailing></div>"
            + "</li>",
            html);
    }

    [Fact]
    public void An_item_states_only_what_it_was_given()
    {
        var plain = Ui.TimelineItem;
        var told = Ui.TimelineItem.Complete.Baseline.Lg;

        var bare = plain.ToHtml();
        var html = told.ToHtml();

        Assert.StartsWith("<li data-ui-timeline-item>", bare, StringComparison.Ordinal);
        Assert.Contains("data-ui-timeline-status=\"complete\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-timeline-align=\"baseline\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-timeline-size=\"lg\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_indicator_carries_a_hidden_first_line_for_baseline_alignment()
    {
        var indicator = Ui.TimelineIndicator["1"];

        var html = indicator.ToHtml();

        Assert.Contains("<div data-ui-timeline-baseline aria-hidden=\"true\">&#x200B;</div><div>1</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_indicator_is_a_grey_circle_of_the_timelines_size()
    {
        var indicator = Ui.TimelineIndicator["1"];

        var html = indicator.ToHtml();

        Assert.Contains("size-(--ui-timeline-indicator-size)", html, StringComparison.Ordinal);
        Assert.Contains("bg-zinc-100", html, StringComparison.Ordinal);
        Assert.Contains("text-zinc-500", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_large_timeline_darkens_the_text_of_its_indicators()
    {
        var timeline = Ui.Timeline.Lg[Ui.TimelineItem[Ui.TimelineIndicator["1"]]];
        var item = Ui.Timeline[Ui.TimelineItem.Lg[Ui.TimelineIndicator["1"]]];

        var whole = timeline.ToHtml();
        var one = item.ToHtml();

        Assert.Contains("text-zinc-800 dark:bg-zinc-700 dark:text-white", whole, StringComparison.Ordinal);
        Assert.Contains("text-zinc-800 dark:bg-zinc-700 dark:text-white", one, StringComparison.Ordinal);
    }

    [Fact]
    public void A_coloured_indicator_is_filled_with_the_hue()
    {
        var indicator = Ui.TimelineIndicator.Color(Ui.Color.Green)["ok"];

        var html = indicator.ToHtml();

        Assert.Contains("bg-green-500", html, StringComparison.Ordinal);
        Assert.Contains("dark:bg-green-600", html, StringComparison.Ordinal);
        Assert.DoesNotContain("bg-zinc-100", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.Color.Amber, "bg-amber-500 text-sm font-semibold text-white dark:text-zinc-950")]
    [InlineData(Ui.Color.Yellow, "dark:bg-yellow-400 dark:text-zinc-950")]
    public void The_light_hues_take_dark_text_in_dark(Ui.Color color, string expected)
    {
        var indicator = Ui.TimelineIndicator.Color(color);

        var html = indicator.ToHtml();

        Assert.Contains(expected, html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_grey_is_not_an_indicator_colour_and_draws_the_plain_circle()
    {
        var indicator = Ui.TimelineIndicator.Color(Ui.Color.Zinc);

        var html = indicator.ToHtml();

        Assert.Contains("bg-zinc-100", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.TimelineStatus.Complete, "bg-zinc-800")]
    [InlineData(Ui.TimelineStatus.Current, "border-2 border-zinc-800")]
    [InlineData(Ui.TimelineStatus.Incomplete, "border-2 border-zinc-100")]
    public void An_indicator_is_drawn_in_its_items_status(Ui.TimelineStatus status, string expected)
    {
        var item = Ui.TimelineItem.Status(status)[Ui.TimelineIndicator["1"]];

        var html = item.ToHtml();

        Assert.Contains(expected, html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_status_is_drawn_instead_of_a_colour()
    {
        var item = Ui.TimelineItem.Complete[Ui.TimelineIndicator.Color(Ui.Color.Green)["1"]];

        var html = item.ToHtml();

        Assert.Contains("bg-zinc-800", html, StringComparison.Ordinal);
        Assert.DoesNotContain("bg-green-500", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_indicators_own_status_overrides_its_items()
    {
        var current = Ui.TimelineItem.Complete[Ui.TimelineIndicator.Status(Ui.TimelineStatus.Current)["1"]];
        var none = Ui.TimelineItem.Complete[Ui.TimelineIndicator.Status(Ui.TimelineStatus.Default)["1"]];

        var ringed = current.ToHtml();
        var plain = none.ToHtml();

        Assert.Contains("border-2 border-zinc-800", ringed, StringComparison.Ordinal);
        Assert.Contains("bg-zinc-100", plain, StringComparison.Ordinal);
        Assert.Contains("data-ui-timeline-status=\"complete\"", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bare_indicator_has_no_circle_whatever_else_it_is_told()
    {
        var item = Ui.TimelineItem.Complete[Ui.TimelineIndicator.Bare.Color(Ui.Color.Red)["1"]];

        var html = item.ToHtml();

        Assert.Contains("<div class=\"grid place-items-center rounded-full text-sm font-semibold\" data-ui-timeline-indicator>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Content_block_and_subgrid_are_marked_divs_that_keep_the_call_sites_attributes()
    {
        var block = Ui.TimelineBlock.Class("rounded-xl").Id("thread")[
            Ui.TimelineSubgrid.Aria("label", "Comment")["avatar", "words"]
        ];

        var html = block.ToHtml();

        Assert.Equal(
            "<div id=\"thread\" class=\"rounded-xl\" data-ui-timeline-block>"
            + "<div data-ui-timeline-subgrid aria-label=\"Comment\">avatarwords</div></div>",
            html);
    }

    [Fact]
    public void A_call_sites_classes_are_added_to_each_part()
    {
        var timeline = Ui.Timeline.Class("[--ui-timeline-item-gap:3rem]")[
            Ui.TimelineItem.Class("mine")[Ui.TimelineIndicator.Class("ring-2")["1"]]
        ];

        var html = timeline.ToHtml();

        Assert.Contains("<ol class=\"[--ui-timeline-item-gap:3rem]\"", html, StringComparison.Ordinal);
        Assert.Contains("<li class=\"mine\"", html, StringComparison.Ordinal);
        Assert.Contains("dark:text-zinc-300 ring-2\"", html, StringComparison.Ordinal);
    }
}
