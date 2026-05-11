namespace Rask.Wasm.Tests.Hosting;

public class PayloadExtractorTests
{
    [Fact]
    public void Extract_EmptyPayload_ReturnsEmptyResult()
    {
        var result = PayloadExtractor.Extract(string.Empty);

        Assert.Equal(string.Empty, result.Html);
        Assert.Null(result.CssHash);
        Assert.Null(result.CssText);
        Assert.Null(result.HistoryJson);
    }

    [Fact]
    public void Extract_HtmlOnly()
    {
        var result = PayloadExtractor.Extract("""{"html":"<p>hi</p>"}""");

        Assert.Equal("<p>hi</p>", result.Html);
        Assert.Null(result.CssHash);
        Assert.Null(result.CssText);
        Assert.Null(result.HistoryJson);
    }

    [Fact]
    public void Extract_HtmlAndCssHash()
    {
        var result = PayloadExtractor.Extract("""{"html":"<p>x</p>","cssHash":"abc"}""");

        Assert.Equal("<p>x</p>", result.Html);
        Assert.Equal("abc", result.CssHash);
        Assert.Null(result.CssText);
    }

    [Fact]
    public void Extract_HtmlCssHashCssText()
    {
        var result = PayloadExtractor.Extract("""{"html":"<p>x</p>","cssHash":"abc","cssText":".x{}"}""");

        Assert.Equal(".x{}", result.CssText);
        Assert.Equal("abc", result.CssHash);
    }

    [Fact]
    public void Extract_HtmlAndHistoryObject_ReturnsRawJson()
    {
        var result = PayloadExtractor.Extract("""{"html":"<p>x</p>","history":{"action":"push","url":"/foo"}}""");

        Assert.NotNull(result.HistoryJson);
        Assert.Contains("\"action\":\"push\"", result.HistoryJson);
        Assert.Contains("\"url\":\"/foo\"", result.HistoryJson);
    }

    [Fact]
    public void Extract_AllFields_ReturnsAll()
    {
        var result = PayloadExtractor.Extract(
            """{"html":"<p>x</p>","cssHash":"h","cssText":".y{}","history":{"action":"replace","url":"/a"}}""");

        Assert.Equal("<p>x</p>", result.Html);
        Assert.Equal("h", result.CssHash);
        Assert.Equal(".y{}", result.CssText);
        Assert.Contains("\"action\":\"replace\"", result.HistoryJson!);
    }

    [Fact]
    public void Extract_NonObjectHistory_ReturnsNullHistory()
    {
        var result = PayloadExtractor.Extract("""{"html":"<p>x</p>","history":"not-an-object"}""");

        Assert.Null(result.HistoryJson);
    }

    [Fact]
    public void Extract_MissingHtml_ReturnsEmptyHtml()
    {
        var result = PayloadExtractor.Extract("""{"cssHash":"abc"}""");

        Assert.Equal(string.Empty, result.Html);
        Assert.Equal("abc", result.CssHash);
    }

    [Fact]
    public void Extract_NumericHtml_FallsBackToEmpty()
    {
        var result = PayloadExtractor.Extract("""{"html":42}""");

        Assert.Equal(string.Empty, result.Html);
    }
}
