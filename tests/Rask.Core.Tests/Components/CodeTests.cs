namespace Rask.Core.Tests.Components;

public partial class CodeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<code></code>", Code.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<code id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></code>",
            Code.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<code>&lt;x&gt;</code>", Code["<x>"].ToHtml());
}
