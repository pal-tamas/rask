namespace Rask.Core.Tests;

public partial class TextTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_PlainString_ReturnsSameString() => Assert.Equal("hello world", Text.Value("hello world").ToHtml());

    [Fact]
    public void Render_AngleBrackets_EncodesToEntities() => Assert.Equal("&lt;x&gt;", Text.Value("<x>").ToHtml());

    [Fact]
    public void Render_DoubleQuotes_EncodesToEntity() => Assert.Equal("a&quot;b", Text.Value("a\"b").ToHtml());

    [Fact]
    public void Render_EmptyString_ReturnsEmptyString() => Assert.Equal("", Text.Value("").ToHtml());

    [Fact]
    public void Factory_ReturnsTextInstanceWithEncodedValue() =>
        Assert.Equal("&lt;x&gt;", Text.Value("<x>").ToHtml());

    [Fact]
    public void Factory_PlainString_RendersUnchanged() =>
        Assert.Equal("hello", Text.Value("hello").ToHtml());
}
