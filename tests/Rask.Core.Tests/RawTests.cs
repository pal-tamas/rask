namespace Rask.Core.Tests;

public partial class RawTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_HtmlInput_ReturnsVerbatim() => Assert.Equal("<b>x</b>", Raw.Value("<b>x</b>").ToHtml());

    [Fact]
    public void Render_EmptyString_ReturnsEmptyString() => Assert.Equal("", Raw.Value("").ToHtml());

    [Fact]
    public void GeneratedFactory_ReturnsRawInstanceWithVerbatimHtml() =>
        Assert.Equal("<b>x</b>", Raw.Value("<b>x</b>").ToHtml());

    [Fact]
    public void GeneratedFactory_EmptyString_ReturnsEmpty() =>
        Assert.Equal("", Raw.Value("").ToHtml());
}
