using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

// A field's .NET regular expression as an input's `pattern`: written only where the browser (a `v`-flag RegExp over the
// whole text) reads it the same way, so the browser never refuses a text the field takes.
public sealed class HtmlPatternTests
{
    [Theory]
    [InlineData(@"^[A-Z]{2,3}$", @"[A-Z]{2,3}")]
    [InlineData(@"\d{4}", @"\p{Nd}{4}")]
    [InlineData(@"[a-z0-9_]+", @"[a-z0-9_]+")]
    [InlineData(@"^\+?[0-9 ]+$", @"\+?[0-9 ]+")]
    [InlineData(@"[a-z-]+", @"[a-z\-]+")]
    [InlineData(@"[.,;]", @"[\.\,\;]")]
    [InlineData(@".+@.+", @"[^\n]+@[^\n]+")]
    [InlineData(@"^a|b$", @"^a|b$")]
    [InlineData(@"(?:ab)+c(?!d)", @"(?:ab)+c(?!d)")]
    [InlineData(@"\p{Lu}\s\-", @"\p{Lu}[\s\x85]-")]
    [InlineData(@"é\x41", @"é\x41")]
    public void A_regex_the_browser_reads_alike_is_written_in_its_dialect(string regex, string expected)
    {
        var written = HtmlPattern.From(regex);

        Assert.Equal(expected, written);
    }

    [Theory]
    [InlineData(@"(?<=a)b")]
    [InlineData(@"(?<!a)b")]
    [InlineData(@"(?<year>\d{4})")]
    [InlineData(@"(?'year'\d{4})")]
    [InlineData(@"(?<y>a)\k<y>")]
    [InlineData(@"(?i)abc")]
    [InlineData(@"(?i:abc)")]
    [InlineData(@"(?>a+)b")]
    [InlineData(@"(?(a)b|c)")]
    [InlineData(@"\Ahello\z")]
    [InlineData(@"hello\Z")]
    [InlineData(@"\Ga")]
    [InlineData(@"[a-z-[aeiou]]")]
    [InlineData(@"[a-z&&[^q]]")]
    [InlineData(@"(unclosed")]
    [InlineData(@"a{,3}")]
    [InlineData(@"\w+")]
    [InlineData(@"\bword\b")]
    [InlineData(@"\S+")]
    [InlineData(@"\p{IsGreek}")]
    [InlineData(@"(a)\1")]
    [InlineData(@".{2,10}")]
    [InlineData(@"[^a]{3}")]
    [InlineData(@".+.+")]
    [InlineData(@"(.+)")]
    [InlineData(@"[]a]")]
    [InlineData(@"a{")]
    [InlineData(@"[^\s]")]
    [InlineData(@"(?=a)*")]
    [InlineData(@"(?#comment)a")]
    [InlineData("😀")]
    [InlineData("")]
    public void A_regex_the_browser_would_read_otherwise_is_not_written(string regex)
    {
        var written = HtmlPattern.From(regex);

        Assert.Null(written);
    }

    [Fact]
    public void A_pattern_found_anywhere_becomes_one_the_whole_text_matches()
    {
        var anchored = HtmlPattern.Found(@"^[A-Z]{3}$", trimmed: false);
        var loose = HtmlPattern.Found(@"[0-9]", trimmed: false);
        var alternated = HtmlPattern.Found(@"^a|b$", trimmed: false);

        Assert.Equal(@"[A-Z]{3}", anchored);
        Assert.Equal(@"[\s\S]*[0-9][\s\S]*", loose);
        Assert.Equal(@"[\s\S]*(?:^a|b$)[\s\S]*", alternated);
        Assert.Equal(@"[\s\S]*[0-9][\s\S]*", HtmlPattern.From(loose));
    }

    [Fact]
    public void A_trimmed_text_takes_the_blanks_around_what_it_must_match()
    {
        var anchored = HtmlPattern.Found(@"^[A-Z]{3}$", trimmed: true);
        var inner = HtmlPattern.Found(@"a$|b", trimmed: true);
        var lookahead = HtmlPattern.Found(@"a(?!\s)", trimmed: true);

        Assert.Equal(@"\s*[A-Z]{3}\s*", anchored);
        Assert.Equal(@"[\s\x85]*[A-Z]{3}[\s\x85]*", HtmlPattern.From(anchored));
        Assert.Null(inner);
        Assert.Null(lookahead);
    }

    [Fact]
    public void An_expression_dotnet_cannot_read_is_found_as_nothing()
    {
        var found = HtmlPattern.Found(@"(?<n>a)", trimmed: false);

        Assert.Null(found);
    }
}
