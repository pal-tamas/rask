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
}
