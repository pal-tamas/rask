using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class FragmentTests
{
    [Fact]
    public void Render_NoChildren_EmitsEmptyString() => Assert.Equal("", new Fragment().ToHtml());

    [Fact]
    public void Render_SingleChild_EmitsThatChild() =>
        Assert.Equal("<!DOCTYPE html>", new Fragment(new Doctype()).ToHtml());

    [Fact]
    public void Render_MultipleChildren_EmitsConcatenated()
    {
        var fragment = new Fragment(new Doctype(), new Html(null));
        Assert.Equal("<!DOCTYPE html><html></html>", fragment.ToHtml());
    }

    [Fact]
    public void Render_TextChild_HtmlEncodes() => Assert.Equal("a&lt;b", new Fragment(new Text("a<b")).ToHtml());
}
