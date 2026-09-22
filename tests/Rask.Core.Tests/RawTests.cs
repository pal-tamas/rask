namespace Rask.Core.Tests;

public partial class RawTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Rendering_html_input_gives_it_verbatim() => Assert.Equal("<b>x</b>", Raw.Value("<b>x</b>").ToHtml());

    [Fact]
    public void Rendering_an_empty_string_gives_an_empty_string() => Assert.Equal("", Raw.Value("").ToHtml());

    [Fact]
    public void The_generated_factory_gives_a_raw_instance_with_verbatim_html() =>
        Assert.Equal("<b>x</b>", Raw.Value("<b>x</b>").ToHtml());

    [Fact]
    public void The_generated_factory_with_an_empty_string_gives_empty_output() =>
        Assert.Equal("", Raw.Value("").ToHtml());
}
