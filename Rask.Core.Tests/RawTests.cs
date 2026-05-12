namespace Rask.Core.Tests;

public class RawTests
{
    [Fact]
    public void Render_HtmlInput_ReturnsVerbatim() => Assert.Equal("<b>x</b>", new Raw("<b>x</b>").ToHtml());

    [Fact]
    public void Render_EmptyString_ReturnsEmptyString() => Assert.Equal("", new Raw("").ToHtml());

    [Fact]
    public void TagsFactory_ReturnsRawInstanceWithVerbatimHtml() =>
        Assert.Equal("<b>x</b>", Tags.Raw("<b>x</b>").ToHtml());

    [Fact]
    public void TagsFactory_EmptyString_ReturnsEmpty() =>
        Assert.Equal("", Tags.Raw("").ToHtml());
}
