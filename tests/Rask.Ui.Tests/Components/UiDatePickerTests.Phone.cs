using Rask.Testing;
using Rask.UiTests.Flux;

namespace Rask.UiTests.Components;

// The date picker a phone shows: under 640 px Flux's is a sheet over the page that writes nothing until its
// button is pressed. The browser reads how wide it is from the box the calendar draws for it (rask-calendar.ts),
// and what a pick does there is RuntimeHookCalendarTests'.
public partial class UiDatePickerTests
{
    [Fact]
    public void The_buttons_of_the_sheet_are_drawn_for_a_phone_and_for_a_picker_that_confirms()
    {
        var plain = Single(default).ToHtml();

        var confirming = Single(default).WithConfirmation().ToHtml();

        Assert.Contains("hidden max-sm:flex justify-end", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden max-sm:flex", confirming, StringComparison.Ordinal);
        Assert.Contains(">Select date</button>", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void The_popup_is_left_to_the_stylesheet_on_a_phone_and_hangs_from_the_picker_elsewhere()
    {
        var html = Single(default).ToHtml();

        var dialog = System.Text.RegularExpressions.Regex.Match(html, "<dialog[^>]*>").Value;

        Assert.Contains("max-sm:[--rask-place:none]", dialog, StringComparison.Ordinal);
        Assert.Contains("sm:top-[anchor(bottom)]", dialog, StringComparison.Ordinal);
        Assert.Contains("max-sm:backdrop:bg-black/10", dialog, StringComparison.Ordinal);
        Assert.Contains("data-rask-place=\"bottom start 5 0\"", dialog, StringComparison.Ordinal);
        Assert.Matches("style=\"position-anchor:--ui-date-picker-\\d+\"", dialog);
    }

    [Fact]
    public void Only_a_calendar_that_would_change_draws_the_box_the_browser_reads_the_width_from()
    {
        var one = Ui.Calendar.Value(default(DateOnly)).Locale("en-US").On(Today).ToHtml();

        var two = Ui.Calendar.Value(default(DateOnly)).Months(2).Locale("en-US").On(Today).ToHtml();

        Assert.DoesNotContain("data-ui-calendar-narrow", one, StringComparison.Ordinal);
        Assert.Contains("data-ui-calendar-narrow=\"\" data-rask-calendar-part=\"narrow\"", two, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-measure", two, StringComparison.Ordinal);
        Assert.Contains("data-month=\"\" class=\"max-sm:hidden\"", two.Replace("class=\"max-sm:hidden\" data-month=\"\"", "data-month=\"\" class=\"max-sm:hidden\"", StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
