namespace Rask.Core.Tests.Components;

// A `[...]` collection expression targeting Component builds a tagless container (internally a
// Fragment) via Component.__Fragment. These pin the container's rendering: no children => empty string,
// single/multiple children => concatenated with no wrapping element, text children HTML-encoded.
public partial class FragmentTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_fragment_with_no_children_renders_an_empty_string()
    {
        Component empty = Component.__Fragment([]);
        Assert.Equal("", empty.ToHtml());
    }

    [Fact]
    public void A_fragment_with_one_child_renders_just_that_child()
    {
        Component fragment = [Doctype];
        Assert.Equal("<!DOCTYPE html>", fragment.ToHtml());
    }

    [Fact]
    public void A_fragment_with_several_children_renders_them_concatenated()
    {
        Component fragment = [Doctype, Html];
        Assert.Equal("<!DOCTYPE html><html></html>", fragment.ToHtml());
    }

    [Fact]
    public void A_fragments_text_child_is_html_encoded()
    {
        Component fragment = [Text.Value("a<b")];
        Assert.Equal("a&lt;b", fragment.ToHtml());
    }
}
