namespace Rask.UiTests.Components;

/// <summary>
///     Flux's heading: the element its level picks, the size it is drawn at, and the accent.
/// </summary>
public partial class UiHeadingTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_heading_without_a_level_is_a_marked_div_in_the_default_size()
    {
        var heading = Ui.Heading["User profile"];

        var html = heading.ToHtml();

        Assert.Equal(
            "<div class=\"font-medium text-zinc-800 dark:text-white text-sm\" data-ui-heading>User profile</div>",
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

        Assert.Contains("text-fx-accent-content", html, StringComparison.Ordinal);
        Assert.DoesNotContain("text-zinc-800", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_call_sites_class_and_data_are_added_to_the_headings_own()
    {
        var heading = Ui.Heading.Class("mb-1").Data(("testid", "title"))["Orders"];

        var html = heading.ToHtml();

        Assert.Contains("text-sm mb-1\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-heading data-testid=\"title\"", html, StringComparison.Ordinal);
    }
}
