namespace Rask.Core.Tests;

public class RawTests
{
    [Fact]
    public void Render_HtmlInput_ReturnsVerbatim() => Assert.Equal("<b>x</b>", new Raw("<b>x</b>").ToHtml());

    [Fact]
    public void Render_EmptyString_ReturnsEmptyString() => Assert.Equal("", new Raw("").ToHtml());
}
