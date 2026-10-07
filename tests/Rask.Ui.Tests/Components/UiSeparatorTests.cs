namespace Rask.UiTests.Components;

/// <summary><c>Ui.Separator</c> — Flux UI's separator: its markup contract and what each prop writes.</summary>
public sealed class UiSeparatorTests
{
    [Fact]
    public void A_separator_is_one_decorative_line_across()
    {
        var html = Ui.Separator.ToHtml();

        Assert.Equal(
            "<div class=\"h-px w-full bg-zinc-800/15 dark:bg-white/20\" data-orientation=\"horizontal\" data-ui-separator=\"\" role=\"none\"></div>",
            html);
    }

    [Fact]
    public void A_vertical_separator_is_a_line_as_tall_as_its_row()
    {
        var html = Ui.Separator.Vertical().ToHtml();

        Assert.Contains("data-orientation=\"vertical\"", html);
        Assert.Contains("class=\"w-px self-stretch bg-zinc-800/15 dark:bg-white/20\"", html);
    }

    [Fact]
    public void Orientation_says_vertical_the_other_way()
    {
        var byOrientation = Ui.Separator.Orientation(Ui.SeparatorOrientation.Vertical).ToHtml();
        var horizontal = Ui.Separator.Horizontal.ToHtml();

        Assert.Equal(Ui.Separator.Vertical().ToHtml(), byOrientation);
        Assert.Equal(Ui.Separator.ToHtml(), horizontal);
    }

    [Fact]
    public void A_subtle_separator_draws_a_fainter_line()
    {
        var html = Ui.Separator.Subtle.ToHtml();

        Assert.Contains("bg-zinc-800/5 dark:bg-white/10", html);
        Assert.DoesNotContain("bg-zinc-800/15", html);
    }

    [Fact]
    public void Text_sits_between_two_lines()
    {
        var html = Ui.Separator.Text("or").ToHtml();

        Assert.Equal(
            "<div class=\"flex w-full items-center\" data-orientation=\"horizontal\" data-ui-separator=\"\" role=\"none\">"
            + "<div class=\"h-px grow bg-zinc-800/15 dark:bg-white/20\"></div>"
            + "<span class=\"mx-6 text-sm font-medium whitespace-nowrap text-zinc-500 dark:text-zinc-300\">or</span>"
            + "<div class=\"h-px grow bg-zinc-800/15 dark:bg-white/20\"></div>"
            + "</div>",
            html);
    }

    [Fact]
    public void Text_on_a_subtle_separator_keeps_both_lines_faint()
    {
        var html = Ui.Separator.Text("or").Subtle.ToHtml();

        Assert.Equal(2, html.Split("bg-zinc-800/5 dark:bg-white/10").Length - 1);
    }

    [Fact]
    public void Text_is_encoded()
    {
        var html = Ui.Separator.Text("<b>").ToHtml();

        Assert.Contains("&lt;b&gt;", html);
    }

    [Fact]
    public void A_class_of_the_call_site_joins_the_separators_own()
    {
        var html = Ui.Separator.Vertical().Class("my-2").ToHtml();

        Assert.Contains("class=\"w-px self-stretch bg-zinc-800/15 dark:bg-white/20 my-2\"", html);
    }

    [Fact]
    public void The_stylesheet_carries_every_class_a_separator_writes()
    {
        var css = UiStylesheet.Css;

        Assert.All(
            new[] { ".bg-zinc-800\\/15", ".bg-zinc-800\\/5", ".self-stretch", ".w-px", ".h-px", ".text-zinc-500", ".my-2" },
            name => Assert.Contains(name, css));
        Assert.DoesNotContain(".divider", css);
    }
}
