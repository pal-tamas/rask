using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class ArticleTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<article></article>", Article().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<article id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></article>",
            Article(Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<article>&lt;x&gt;</article>", Article()["<x>"].ToHtml());
}
