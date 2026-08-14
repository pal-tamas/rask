namespace Rask.Core.Tests.Components;

// A `[...]` collection expression targeting Component builds a tagless container (internally a
// Fragment) via Component.__Fragment. These pin the container's rendering: no children => empty string,
// single/multiple children => concatenated with no wrapping element, text children HTML-encoded.
public partial class FragmentTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NoChildren_EmitsEmptyString()
    {
        Component empty = Component.__Fragment([]);
        Assert.Equal("", empty.ToHtml());
    }

    [Fact]
    public void Render_SingleChild_EmitsThatChild()
    {
        Component fragment = [Doctype];
        Assert.Equal("<!DOCTYPE html>", fragment.ToHtml());
    }

    [Fact]
    public void Render_MultipleChildren_EmitsConcatenated()
    {
        Component fragment = [Doctype, Html];
        Assert.Equal("<!DOCTYPE html><html></html>", fragment.ToHtml());
    }

    [Fact]
    public void Render_TextChild_HtmlEncodes()
    {
        Component fragment = [Text.Value("a<b")];
        Assert.Equal("a&lt;b", fragment.ToHtml());
    }
}
