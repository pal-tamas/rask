using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class DialogTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<dialog></dialog>", new Dialog().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<dialog id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" open></dialog>",
            new Dialog { Open = true, Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<dialog>&lt;x&gt;</dialog>", new Dialog { Children = ["<x>"] }.ToHtml());
}
