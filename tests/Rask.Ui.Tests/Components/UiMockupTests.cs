namespace Rask.Ui.Tests.Components;

/// <summary>
///     The four mockups. Decoration around a screenshot or a snippet, so the assertions are about
///     structure — and about the one thing that is not decoration: the code block's text.
/// </summary>
public partial class UiMockupTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_browser_mockup_shows_the_url_it_is_given() =>
        Assert.Contains("https://rask.sh",
            UiMockupBrowser.Url("https://rask.sh")[Span["page"]].ToHtml());

    [Fact]
    public void The_browser_mockup_works_without_a_url() =>
        Assert.Contains("mockup-browser", UiMockupBrowser[Span["page"]].ToHtml());

    [Fact]
    public void Each_code_line_carries_its_own_prefix()
    {
        // daisyUI draws the prefix from the attribute, so a line whose prefix went missing renders
        // flush with the others and reads as output rather than as a command.
        var html = UiMockupCode.Lines([("$", "dotnet add package Rask.Ui"), ("$", "dotnet run")])
            .ToHtml();

        Assert.Equal(2, Occurrences(html, "data-prefix=\"$\""));
        Assert.Contains("dotnet add package Rask.Ui", html);
    }

    [Fact]
    public void Code_lines_are_encoded_rather_than_injected()
    {
        // The one place a mockup carries user text. A snippet containing markup has to READ as that
        // markup, not become it.
        var html = UiMockupCode.Lines([("$", "<script>alert(1)</script>")]).ToHtml();

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Each_line_is_a_pre_holding_a_code() =>
        Assert.Contains("<pre", UiMockupCode.Lines([("$", "ls")]).ToHtml());

    [Fact]
    public void The_phone_and_window_mockups_wrap_their_children()
    {
        Assert.Contains("mockup-phone", UiMockupPhone[Span["screen"]].ToHtml());
        Assert.Contains("mockup-window", UiMockupWindow[Span["screen"]].ToHtml());
        Assert.Contains("screen", UiMockupPhone[Span["screen"]].ToHtml());
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}
