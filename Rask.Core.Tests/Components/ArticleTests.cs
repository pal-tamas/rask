using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class ArticleTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<article></article>", new Article(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Article.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<article id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></article>",
            new Article(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<article>&lt;x&gt;</article>", new Article(null, "<x>").ToHtml());
}
