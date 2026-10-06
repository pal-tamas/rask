using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's tooltip: the markup it writes, and above all what joins the trigger to it — the half a
///     screen reader hears, and the half no screenshot shows.
/// </summary>
public partial class UiTooltipTests : global::Rask.Core.RaskMarkup
{
    private const string Look =
        "m-0 rounded-md border-0 bg-zinc-800 px-2.5 py-2 text-xs font-medium text-white "
        + "[position-anchor:--ui-tooltip] dark:border dark:border-white/10 dark:bg-zinc-700";

    [Fact]
    public void A_tooltip_wraps_its_trigger_and_writes_the_content_after_it()
    {
        var html = Ui.Tooltip.Content("Settings")[Button["Open"]].ToHtml();

        var id = ContentId(html);
        Assert.Equal(
            "<div class=\"inline-flex\" data-ui-tooltip>"
            + $"<button aria-describedby=\"{id}\" interestfor=\"{id}\">Open</button>"
            + $"<div id=\"{id}\" class=\"{Look} inset-[5px] [position-area:top] [position-try-fallbacks:flip-block]\""
            + " popover=\"manual\" data-ui-tooltip-content role=\"tooltip\" aria-hidden=\"true\">Settings</div>"
            + "</div>",
            html);
    }

    [Fact]
    public void A_trigger_with_no_text_of_its_own_is_named_by_the_tooltip()
    {
        var html = Ui.Tooltip.Content("Settings")[Button[Ui.Icon.Name(Ui.IconName.Cog6Tooth)]].ToHtml();

        var id = ContentId(html);
        Assert.Contains($"<button aria-labelledby=\"{id}\" interestfor=\"{id}\">", html);
        Assert.DoesNotContain("aria-describedby", html);
    }

    [Fact]
    public void A_trigger_that_is_already_described_keeps_its_own_description_first()
    {
        var html = Ui.Tooltip.Content("Settings")[Button.Aria("describedby", "hint")["Open"]].ToHtml();

        var id = ContentId(html);
        Assert.Contains($"aria-describedby=\"hint {id}\"", html);
    }

    [Fact]
    public void A_trigger_named_by_something_else_is_described_rather_than_renamed()
    {
        var html = Ui.Tooltip.Content("Settings")[Button.Aria("labelledby", "title")[Ui.Icon.Name(Ui.IconName.Cog6Tooth)]].ToHtml();

        var id = ContentId(html);
        Assert.Contains($"aria-describedby=\"{id}\"", html);
        Assert.Contains("aria-labelledby=\"title\"", html);
    }

    [Fact]
    public void A_kit_button_is_wired_exactly_as_a_plain_one()
    {
        var html = Ui.Tooltip.Content("Saves the draft")[Ui.Button["Save"]].ToHtml();

        var id = ContentId(html);
        Assert.Contains($"aria-describedby=\"{id}\"", html);
        Assert.Contains($"interestfor=\"{id}\"", html);
    }

    [Fact]
    public void A_link_is_an_interest_invoker_and_anything_else_is_left_to_the_stylesheet()
    {
        var link = Ui.Tooltip.Content("Opens the guide")[A.Href("/guide")["Guide"]].ToHtml();
        var text = Ui.Tooltip.Content("Not yet")[Span["Soon"]].ToHtml();

        Assert.Contains($"interestfor=\"{ContentId(link)}\"", link);
        Assert.DoesNotContain("interestfor", text);
        Assert.Contains($"<span aria-describedby=\"{ContentId(text)}\">Soon</span>", text);
    }

    [Fact]
    public void Around_something_that_is_not_one_element_the_wrapper_carries_the_description()
    {
        var html = Ui.Tooltip.Content("Search")[Ui.Kbd.Text("K")].ToHtml();

        var id = ContentId(html);
        Assert.StartsWith($"<div class=\"inline-flex\" data-ui-tooltip role=\"group\" aria-describedby=\"{id}\">", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.TooltipPosition.Top, Ui.TooltipAlign.Center, "inset-[5px] [position-area:top] [position-try-fallbacks:flip-block]")]
    [InlineData(Ui.TooltipPosition.Top, Ui.TooltipAlign.Start, "inset-x-0 inset-y-[5px] [position-area:block-start_span-inline-end] [position-try-fallbacks:flip-block]")]
    [InlineData(Ui.TooltipPosition.Top, Ui.TooltipAlign.End, "inset-x-0 inset-y-[5px] [position-area:block-start_span-inline-start] [position-try-fallbacks:flip-block]")]
    [InlineData(Ui.TooltipPosition.Bottom, Ui.TooltipAlign.Center, "inset-[5px] [position-area:bottom] [position-try-fallbacks:flip-block]")]
    [InlineData(Ui.TooltipPosition.Bottom, Ui.TooltipAlign.Start, "inset-x-0 inset-y-[5px] [position-area:block-end_span-inline-end] [position-try-fallbacks:flip-block]")]
    [InlineData(Ui.TooltipPosition.Bottom, Ui.TooltipAlign.End, "inset-x-0 inset-y-[5px] [position-area:block-end_span-inline-start] [position-try-fallbacks:flip-block]")]
    [InlineData(Ui.TooltipPosition.Left, Ui.TooltipAlign.Center, "inset-[5px] [position-area:left] [position-try-fallbacks:flip-inline]")]
    [InlineData(Ui.TooltipPosition.Left, Ui.TooltipAlign.Start, "inset-x-[5px] inset-y-0 [position-area:left_span-bottom] [position-try-fallbacks:flip-inline]")]
    [InlineData(Ui.TooltipPosition.Left, Ui.TooltipAlign.End, "inset-x-[5px] inset-y-0 [position-area:left_span-top] [position-try-fallbacks:flip-inline]")]
    [InlineData(Ui.TooltipPosition.Right, Ui.TooltipAlign.Center, "inset-[5px] [position-area:right] [position-try-fallbacks:flip-inline]")]
    [InlineData(Ui.TooltipPosition.Right, Ui.TooltipAlign.Start, "inset-x-[5px] inset-y-0 [position-area:right_span-bottom] [position-try-fallbacks:flip-inline]")]
    [InlineData(Ui.TooltipPosition.Right, Ui.TooltipAlign.End, "inset-x-[5px] inset-y-0 [position-area:right_span-top] [position-try-fallbacks:flip-inline]")]
    public void Every_position_and_alignment_is_a_side_of_the_trigger_that_flips_when_it_does_not_fit(
        Ui.TooltipPosition position, Ui.TooltipAlign align, string expected)
    {
        var html = Ui.Tooltip.Content("Settings").Position(position).Align(align)[Button["Open"]].ToHtml();

        Assert.Contains($"class=\"{Look} {expected}\"", html);
    }

    [Fact]
    public void The_stylesheet_carries_every_side_the_component_can_write()
    {
        var css = UiStylesheet.Css;

        // Arbitrary properties are only emitted for the literals Tailwind read whole; a side missing here is
        // a tooltip that opens in the corner of the page.
        Assert.All(
            ["top", "bottom", "left", "right", "block-start span-inline-end", "block-end span-inline-start", "left span-bottom", "right span-top"],
            area => Assert.Contains("position-area:" + area, css));
        Assert.Contains("anchor-scope:--ui-tooltip", css);
        Assert.Contains("[data-ui-tooltip]>[interestfor]{interest-delay:0s}", css);
    }

    [Fact]
    public void A_gap_is_the_inset_that_faces_the_trigger_and_an_offset_slides_it_along_that_side()
    {
        var above = Ui.Tooltip.Content("Settings").Gap(12).Offset(20)[Button["Open"]].ToHtml();
        var beside = Ui.Tooltip.Content("Settings").Right.Gap(12).Offset(20)[Button["Open"]].ToHtml();

        Assert.Contains("style=\"bottom:12px;translate:20px 0\"", above);
        Assert.Contains("style=\"left:12px;translate:0 20px\"", beside);
    }

    [Fact]
    public void An_offset_runs_the_other_way_from_the_end_edge()
    {
        var html = Ui.Tooltip.Content("Settings").End.Offset(8)[Button["Open"]].ToHtml();

        // Measured on Flux: it moves away from the edge it is aligned to.
        Assert.Contains("style=\"translate:-8px 0\"", html);
    }

    [Fact]
    public void A_tooltip_with_neither_gap_nor_offset_writes_no_style()
    {
        var html = Ui.Tooltip.Content("Settings")[Button["Open"]].ToHtml();

        Assert.DoesNotContain("style=", html);
    }

    [Fact]
    public void A_shortcut_follows_the_content_in_a_quieter_colour()
    {
        var html = Ui.Tooltip.Content("Toggle dark mode").Kbd("D")[Button["Theme"]].ToHtml();

        Assert.Contains(">Toggle dark mode <span class=\"ps-1 text-zinc-300\">D</span></div>", html);
    }

    [Fact]
    public void Richer_content_is_a_part_written_after_the_trigger()
    {
        var html = Ui.Tooltip[
            Button["Tax id"],
            Ui.TooltipContent.Class("max-w-[20rem]").Kbd("T")[P["Nine digits."]]
        ].ToHtml();

        var id = ContentId(html);
        Assert.Contains($"<button aria-describedby=\"{id}\" interestfor=\"{id}\">Tax id</button><div id=\"{id}\"", html);
        Assert.Contains("[position-try-fallbacks:flip-block] max-w-[20rem]\"", html);
        Assert.Contains("<p>Nine digits.</p> <span class=\"ps-1 text-zinc-300\">T</span></div>", html);
    }

    [Fact]
    public void A_toggleable_tooltip_makes_a_button_the_popover_target_of_its_content()
    {
        var html = Ui.Tooltip.Content("Nine digits").Toggleable()[Button["Why"]].ToHtml();

        var id = ContentId(html);
        Assert.StartsWith("<div class=\"inline-flex\" data-ui-tooltip data-toggleable>", html, StringComparison.Ordinal);
        Assert.Contains($"<button aria-controls=\"{id}\" popovertarget=\"{id}\">Why</button>", html);
        Assert.Contains("popover=\"auto\" data-ui-tooltip-content>Nine digits</div>", html);
        Assert.DoesNotContain("interestfor", html);
        Assert.DoesNotContain("role=\"tooltip\"", html);
    }

    [Fact]
    public void A_toggleable_tooltip_around_something_that_cannot_be_clicked_open_takes_focus_instead()
    {
        var html = Ui.Tooltip.Content("Nine digits").Toggleable()[Ui.Icon.Name(Ui.IconName.InformationCircle)].ToHtml();

        var id = ContentId(html);
        Assert.StartsWith(
            $"<div class=\"inline-flex\" data-ui-tooltip data-toggleable role=\"group\" tabindex=\"0\" aria-describedby=\"{id}\">",
            html,
            StringComparison.Ordinal);
        Assert.Contains("popover=\"manual\" data-ui-tooltip-content role=\"tooltip\"", html);
    }

    [Fact]
    public void A_toggleable_tooltip_around_an_element_that_is_not_a_button_speaks_from_the_wrapper_that_takes_the_focus()
    {
        var html = Ui.Tooltip.Content("Nine digits").Toggleable()[Span["?"]].ToHtml();

        var id = ContentId(html);
        Assert.Contains($"role=\"group\" tabindex=\"0\" aria-describedby=\"{id}\"><span>?</span>", html);
    }

    [Fact]
    public void A_disabled_tooltip_never_shows_and_its_trigger_keeps_the_description()
    {
        var html = Ui.Tooltip.Content("Settings").Disabled()[Button["Open"]].ToHtml();

        var id = ContentId(html);
        Assert.StartsWith("<div class=\"inline-flex\" data-ui-tooltip data-disabled>", html, StringComparison.Ordinal);
        Assert.Contains($"<button aria-describedby=\"{id}\">Open</button>", html);
        Assert.DoesNotContain("interestfor", html);
    }

    [Fact]
    public void A_disabled_toggleable_tooltip_cannot_be_clicked_open()
    {
        var html = Ui.Tooltip.Content("Nine digits").Toggleable().Disabled()[Button["Why"]].ToHtml();

        Assert.DoesNotContain("popovertarget", html);
    }

    [Fact]
    public void An_interactive_tooltip_is_controlled_by_its_trigger_and_stays_in_the_reading_order()
    {
        var html = Ui.Tooltip.Content("Settings").Interactive()[Button["Open"]].ToHtml();

        var id = ContentId(html);
        Assert.Contains($"<button aria-controls=\"{id}\" interestfor=\"{id}\">Open</button>", html);
        Assert.DoesNotContain("aria-hidden", html);
        Assert.DoesNotContain("aria-describedby", html);
    }

    [Fact]
    public void Rendering_it_again_wires_the_same_trigger_once()
    {
        var tooltip = Ui.Tooltip.Content("Settings")[Button.Aria("describedby", "hint")["Open"]];

        var first = tooltip.ToHtml();
        var second = tooltip.ToHtml();

        Assert.Equal(first, second);
        Assert.Contains($"aria-describedby=\"hint {ContentId(first)}\"", second);
    }

    [Fact]
    public void Two_tooltips_never_share_an_id()
    {
        var first = Ui.Tooltip.Content("One")[Button["1"]].ToHtml();
        var second = Ui.Tooltip.Content("Two")[Button["2"]].ToHtml();

        Assert.NotEqual(ContentId(first), ContentId(second));
    }

    [Fact]
    public void A_class_from_the_call_site_lands_on_the_wrapper()
    {
        var html = Ui.Tooltip.Content("Settings").Class("ms-auto")[Button["Open"]].ToHtml();

        Assert.StartsWith("<div class=\"inline-flex ms-auto\" data-ui-tooltip>", html, StringComparison.Ordinal);
    }

    private static string ContentId(string html) => ContentIdPattern().Match(html).Groups[1].Value;

    [GeneratedRegex("<div id=\"(ui-tooltip-\\d+)\"")]
    private static partial Regex ContentIdPattern();
}
