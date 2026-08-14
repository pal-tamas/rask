namespace Rask.Core.Tests.Components;

public partial class OlTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<ol></ol>", Ol.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<ol id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" type=\"1\" reversed start=\"5\"></ol>",
            Ol
                .Type("1")
                .Reversed(true)
                .Start(5)
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<ol>&lt;x&gt;</ol>", Ol["<x>"].ToHtml());
}
