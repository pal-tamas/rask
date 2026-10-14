using Rask.Core;
using static Rask.Markup;

namespace Rask.Printing.Tests;

public sealed class PrintTests
{
    [Fact]
    public void Only_wraps_its_children_in_the_paper_only_class()
    {
        var html = Print.Only(P["Printed on 14 October"]).ToHtml();

        Assert.Equal("<div class=\"rask-print-only\"><p>Printed on 14 October</p></div>", html);
    }

    [Fact]
    public void Hide_wraps_its_children_in_the_never_on_paper_class()
    {
        var html = Print.Hide(Nav["menu"], P["more"]).ToHtml();

        Assert.Equal("<div class=\"rask-print-hide\"><nav>menu</nav><p>more</p></div>", html);
    }

    [Fact]
    public void Styles_without_a_page_carry_only_the_two_print_rules()
    {
        var html = Print.Styles().ToHtml();

        Assert.Equal(
            "<style>.rask-print-only{display:none}@media print{.rask-print-only{display:block}.rask-print-hide{display:none!important}}</style>",
            html);
    }

    [Fact]
    public void Styles_write_the_page_rule_for_size_orientation_and_margins()
    {
        var html = Print.Styles(PrintPage.Size("A4").Landscape().Margins("15mm")).ToHtml();

        Assert.EndsWith("@page{size:A4 landscape;margin:15mm;}</style>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Letter", "@page{size:Letter;}")]
    [InlineData("210mm 297mm", "@page{size:210mm 297mm;}")]
    public void A_named_paper_or_a_size_in_lengths_is_written_as_given(string size, string rule)
    {
        var html = Print.Styles(PrintPage.Size(size)).ToHtml();

        Assert.Contains(rule, html, StringComparison.Ordinal);
    }

    [Fact]
    public void Landscape_alone_and_portrait_after_it_are_written_as_they_end_up()
    {
        var landscape = Print.Styles(default(PrintPage).Landscape()).ToHtml();
        var portrait = Print.Styles(PrintPage.Size("A5").Landscape().Portrait()).ToHtml();

        Assert.Contains("@page{size:landscape;}", landscape, StringComparison.Ordinal);
        Assert.Contains("@page{size:A5;}", portrait, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("A4;}</style><script>alert(1)</script>")]
    [InlineData("url(http://evil)")]
    [InlineData("")]
    [InlineData("   ")]
    public void Text_that_is_not_a_paper_or_a_length_is_refused_before_it_reaches_a_stylesheet(string text)
    {
        Assert.Throws<ArgumentException>(() => PrintPage.Size(text));
        Assert.Throws<ArgumentException>(() => default(PrintPage).Margins(text));
    }

    [Fact]
    public void A_print_button_carries_a_click_handler_and_keeps_its_label()
    {
        var button = Button.Print();

        var html = button["Print this page"].ToHtml();

        Assert.True(button.OnClick.HasValue);
        Assert.Equal("<button>Print this page</button>", html);
    }
}
