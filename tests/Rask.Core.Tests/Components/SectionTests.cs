namespace Rask.Core.Tests.Components;

public partial class SectionTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<section></section>", Section.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<section id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></section>",
            Section.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<section>&lt;x&gt;</section>", Section["<x>"].ToHtml());
}
