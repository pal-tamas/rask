namespace Rask.Core.Tests.Components;

public class FragmentTests
{
    [Fact]
    public void Render_NoChildren_EmitsEmptyString() => Assert.Equal("", Fragment().ToHtml());

    [Fact]
    public void Render_SingleChild_EmitsThatChild() =>
        Assert.Equal("<!DOCTYPE html>", Fragment()[Doctype()].ToHtml());

    [Fact]
    public void Render_MultipleChildren_EmitsConcatenated()
    {
        var fragment = Fragment()[Doctype(), Html()];
        Assert.Equal("<!DOCTYPE html><html></html>", fragment.ToHtml());
    }

    [Fact]
    public void Render_TextChild_HtmlEncodes() => Assert.Equal("a&lt;b", Fragment()[Text("a<b")].ToHtml());
}
