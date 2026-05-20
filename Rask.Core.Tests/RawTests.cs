namespace Rask.Core.Tests;

public class RawTests
{
    [Fact]
    public void Render_HtmlInput_ReturnsVerbatim() => Assert.Equal("<b>x</b>", Raw("<b>x</b>").ToHtml());

    [Fact]
    public void Render_EmptyString_ReturnsEmptyString() => Assert.Equal("", Raw("").ToHtml());

    [Fact]
    public void GeneratedFactory_ReturnsRawInstanceWithVerbatimHtml() =>
        Assert.Equal("<b>x</b>", Raw("<b>x</b>").ToHtml());

    [Fact]
    public void GeneratedFactory_EmptyString_ReturnsEmpty() =>
        Assert.Equal("", Raw("").ToHtml());
}
