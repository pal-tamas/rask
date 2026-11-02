namespace Rask.UiTests.Components;

/// <summary>
///     Flux's heading: the element its level picks, the size it is drawn at, and the accent.
/// </summary>
public partial class UiHeadingTests : global::Rask.Core.RaskMarkup
{
    // Flux's own rule for the 8px between a heading and a subheading next to it, HTML-encoded as it is written.
    internal const string NextToSubheading =
        "[&amp;:has(&#x2B;[data-ui-subheading])]:mb-2 [[data-ui-subheading]&#x2B;&amp;]:mt-2";

    [Fact]
    public void A_heading_without_a_level_is_a_marked_div_in_the_default_size()
    {
        var heading = Ui.Heading["User profile"];

        var html = heading.ToHtml();

        Assert.Equal(
            "<div class=\"font-medium text-zinc-800 dark:text-white text-sm " + NextToSubheading + "\" data-ui-heading>User profile</div>",
            html);
    }

    [Theory]
    [InlineData(1, "<h1 ")]
    [InlineData(3, "<h3 ")]
    [InlineData(4, "<h4 ")]
    [InlineData(5, "<div ")]
    [InlineData(6, "<div ")]
    public void A_level_is_the_element_and_leaves_the_size_alone(int level, string expected)
    {
        var heading = Ui.Heading.Level(level).Xl["Orders"];

        var html = heading.ToHtml();

        Assert.StartsWith(expected, html, StringComparison.Ordinal);
        Assert.Contains("text-2xl", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.HeadingSize.Base, "text-sm")]
    [InlineData(Ui.HeadingSize.Lg, "text-base")]
    [InlineData(Ui.HeadingSize.Xl, "text-2xl")]
    [InlineData(Ui.HeadingSize.Xxl, "text-4xl")]
    public void Each_size_writes_one_step_of_the_scale(Ui.HeadingSize size, string expected)
    {
        var heading = Ui.Heading.Size(size)["Orders"];

        var classes = heading.ToHtml().Split('"')[1].Split(' ');

        Assert.Contains(expected, classes);
        Assert.Single(classes, name => name.StartsWith("text-", StringComparison.Ordinal) && !name.StartsWith("text-zinc", StringComparison.Ordinal));
    }

    [Fact]
    public void An_accented_heading_takes_the_accent_instead_of_the_ink()
    {
        var heading = Ui.Heading.Accent()["Orders"];

        var html = heading.ToHtml();

        Assert.Contains("text-accent-content", html, StringComparison.Ordinal);
        Assert.DoesNotContain("text-zinc-800", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_call_sites_class_and_data_are_added_to_the_headings_own()
    {
        var heading = Ui.Heading.Class("mb-1").Data(("testid", "title"))["Orders"];

        var html = heading.ToHtml();

        Assert.Contains(NextToSubheading + " mb-1\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-heading data-testid=\"title\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_subheading_is_a_marked_div_of_small_quiet_text_an_app_can_recolour()
    {
        var subheading = Ui.Subheading.Class("text-center")["Make changes to your personal details."];

        var html = subheading.ToHtml();

        Assert.Equal(
            "<div class=\"text-sm [:where(&amp;)]:text-zinc-500 [:where(&amp;)]:dark:text-white/70 text-center\" data-ui-subheading>"
            + "Make changes to your personal details.</div>",
            html);
    }

    [Fact]
    public void A_heading_keeps_8px_from_a_subheading_on_either_side_of_it()
    {
        var heading = Ui.Heading.Lg["Update profile"];

        var html = heading.ToHtml();

        Assert.Contains(NextToSubheading, html, StringComparison.Ordinal);
    }
}
