namespace Rask.Core.Tests;

public class TextTests
{
    [Fact]
    public void Render_PlainString_ReturnsSameString() => Assert.Equal("hello world", new Text("hello world").ToHtml());

    [Fact]
    public void Render_AngleBrackets_EncodesToEntities() => Assert.Equal("&lt;x&gt;", new Text("<x>").ToHtml());

    [Fact]
    public void Render_DoubleQuotes_EncodesToEntity() => Assert.Equal("a&quot;b", new Text("a\"b").ToHtml());

    [Fact]
    public void Render_EmptyString_ReturnsEmptyString() => Assert.Equal("", new Text("").ToHtml());

    [Fact]
    public void TagsFactory_ReturnsTextInstanceWithEncodedValue() =>
        Assert.Equal("&lt;x&gt;", Tags.Text("<x>").ToHtml());

    [Fact]
    public void TagsFactory_PlainString_RendersUnchanged() =>
        Assert.Equal("hello", Tags.Text("hello").ToHtml());
}
