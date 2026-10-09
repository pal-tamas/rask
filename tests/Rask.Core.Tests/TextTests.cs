#pragma warning disable RASK014 // a test-defined component subclass has no generated chain entry

namespace Rask.Core.Tests;

public partial class TextTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Rendering_a_plain_string_gives_the_same_string() => Assert.Equal("hello world", Text.Value("hello world").ToHtml());

    [Fact]
    public void Rendering_angle_brackets_encodes_them_to_entities() => Assert.Equal("&lt;x&gt;", Text.Value("<x>").ToHtml());

    [Fact]
    public void Rendering_double_quotes_encodes_them_to_an_entity() => Assert.Equal("a&quot;b", Text.Value("a\"b").ToHtml());

    [Fact]
    public void Rendering_an_empty_string_gives_an_empty_string() => Assert.Equal("", Text.Value("").ToHtml());

    [Fact]
    public void The_factory_gives_a_text_instance_with_the_encoded_value() =>
        Assert.Equal("&lt;x&gt;", Text.Value("<x>").ToHtml());

    [Fact]
    public void A_plain_string_from_the_factory_renders_unchanged() =>
        Assert.Equal("hello", Text.Value("hello").ToHtml());

    // The spelling every other tag takes. It used to render nothing at all: Text showed its Value and dropped
    // whatever the indexer handed it.
    [Fact]
    public void Words_handed_over_as_children_are_rendered() => Assert.Equal("hello world", Text["hello world"].ToHtml());

    [Fact]
    public void Words_handed_over_as_children_are_encoded() => Assert.Equal("&lt;x&gt;", Text["<x>"].ToHtml());

    [Fact]
    public void Several_children_render_in_order_as_one_run_of_text() =>
        Assert.Equal("3 of 12", Text[3, " of ", 12].ToHtml());

    [Fact]
    public void A_value_and_children_together_render_the_value_first() =>
        Assert.Equal("page 3", Text.Value("page ")[3].ToHtml());

    [Fact]
    public void An_empty_child_renders_an_empty_string() => Assert.Equal("", Text[""].ToHtml());

    [Fact]
    public void A_text_that_stops_passing_children_does_not_keep_the_ones_it_had()
    {
        var page = new SwitchingText();
        var withChildren = page.RenderAsLiveRoot();

        page.Bracketed = false;
        var withValue = page.RenderAsLiveRoot();

        Assert.Contains("<p>first</p>", withChildren);
        Assert.Contains("<p>second</p>", withValue);
    }

    private sealed partial class SwitchingText : Component
    {
        public bool Bracketed { get; set; } = true;

        protected override Component? Render() => P[Bracketed ? Text["first"] : Text.Value("second")];
    }
}
