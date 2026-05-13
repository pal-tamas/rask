namespace Rask.Core.Tests;

public class TextTests
{
    [Fact]
    public void Render_PlainString_ReturnsSameString() => Assert.Equal("hello world", Text("hello world").ToHtml());

    [Fact]
    public void Render_AngleBrackets_EncodesToEntities() => Assert.Equal("&lt;x&gt;", Text("<x>").ToHtml());

    [Fact]
    public void Render_DoubleQuotes_EncodesToEntity() => Assert.Equal("a&quot;b", Text("a\"b").ToHtml());

    [Fact]
    public void Render_EmptyString_ReturnsEmptyString() => Assert.Equal("", Text("").ToHtml());

    [Fact]
    public void Factory_ReturnsTextInstanceWithEncodedValue() =>
        Assert.Equal("&lt;x&gt;", Text("<x>").ToHtml());

    [Fact]
    public void Factory_PlainString_RendersUnchanged() =>
        Assert.Equal("hello", Text("hello").ToHtml());
}
