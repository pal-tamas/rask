using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     The theme picker behind a trigger is a popover, so it closes on Escape and on a click outside.
/// </summary>
/// <remarks>
///     It was a <c>&lt;details&gt;</c>, which closes on nothing but its own summary. The closing itself is the
///     browser's and only a real one proves it (<c>SiteExampleTests</c>); what a unit test can hold is the shape
///     that earns it — a <c>popover="auto"</c> panel the trigger names, and no disclosure left behind.
/// </remarks>
public partial class UiThemeDropdownTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void It_is_a_popover_rather_than_a_disclosure()
    {
        var html = Ui.ThemeDropdown.ToHtml();

        Assert.DoesNotContain("<details", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<summary", html, StringComparison.Ordinal);
        Assert.Contains("popover=\"auto\"", html, StringComparison.Ordinal);

        // The trigger opens exactly the panel it sits beside.
        var target = Regex.Match(html, "popovertarget=\"(uipop-\\d+-panel)\"");
        Assert.True(target.Success, html);
        Assert.Contains($"id=\"{target.Groups[1].Value}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_radios_are_inside_the_panel()
    {
        var html = Ui.ThemeDropdown.ToHtml();

        var panel = html.IndexOf("popover=\"auto\"", StringComparison.Ordinal);
        var firstRadio = html.IndexOf("theme-controller", StringComparison.Ordinal);
        Assert.True(panel >= 0 && firstRadio > panel, html);
    }

    [Fact]
    public void The_list_is_not_a_css_dropdown()
    {
        // daisyUI's dropdown-content is revealed on the wrapper's :focus-within, so on a popover it shows the
        // list over the trigger the moment the trigger takes focus — and the click lands on the list.
        var html = Ui.ThemeDropdown.ToHtml();

        Assert.DoesNotContain("dropdown-content", html, StringComparison.Ordinal);
        Assert.Contains("class=\"menu w-52 flex-nowrap p-0\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_scrolls_with_the_list_inset_it_had()
    {
        var html = Ui.ThemeDropdown.ToHtml();

        Assert.Contains("max-h-96 overflow-y-auto p-2!", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_trigger_keeps_its_label_and_size()
    {
        Assert.Contains("<span>Theme</span>", Ui.ThemeDropdown.ToHtml(), StringComparison.Ordinal);
        Assert.Contains("<span>Look</span>", Ui.ThemeDropdown.Trigger("Look").ToHtml(), StringComparison.Ordinal);
        Assert.Contains("class=\"btn btn-sm\"", Ui.ThemeDropdown.ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void Aligned_to_the_end_it_opens_back_over_the_page()
    {
        Assert.Contains("position-area:block-end span-inline-start",
            Ui.ThemeDropdown.Align(Ui.Align.End).ToHtml(), StringComparison.Ordinal);
    }
}
