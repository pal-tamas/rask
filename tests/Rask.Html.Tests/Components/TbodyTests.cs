namespace Rask.Html.Tests.Components;

public partial class TbodyTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<tbody></tbody>", Tbody.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<tbody id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></tbody>",
            Tbody.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<tbody>&lt;x&gt;</tbody>", Tbody["<x>"].ToHtml());
}
