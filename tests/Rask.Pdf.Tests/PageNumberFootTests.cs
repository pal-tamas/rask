namespace Rask.Printing.Tests;

/// <summary>The page numbers at the foot: the catalog's text in the caller's language, Chromium's spans in its values.</summary>
public sealed class PageNumberFootTests
{
    [Fact]
    public void An_english_caller_gets_the_page_out_of_the_total()
    {
        const string Language = "en-US";

        var foot = PrintCulture.In(Language, PageNumberFoot.Html);

        Assert.Equal("""<span class="pageNumber"></span> / <span class="totalPages"></span>""", foot);
    }

    [Fact]
    public void A_hungarian_caller_gets_the_formal_hungarian_from_the_catalog()
    {
        const string Language = "hu-HU";

        var foot = PrintCulture.In(Language, PageNumberFoot.Html);

        Assert.Equal("""<span class="pageNumber"></span> / <span class="totalPages"></span> oldal""", foot);
    }

    [Fact]
    public void The_words_around_the_numbers_are_escaped_and_only_the_values_become_spans()
    {
        var text = $"<b>Lap</b> {PageNumberFoot.Page} & {PageNumberFoot.Pages}";

        var foot = PageNumberFoot.Html(text);

        Assert.Equal("""&lt;b&gt;Lap&lt;/b&gt; <span class="pageNumber"></span> &amp; <span class="totalPages"></span>""", foot);
    }
}
