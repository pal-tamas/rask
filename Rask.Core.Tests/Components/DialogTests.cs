using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class DialogTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<dialog></dialog>", new Dialog(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Dialog.Props(true,
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<dialog id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" open></dialog>",
            new Dialog(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<dialog>&lt;x&gt;</dialog>", new Dialog(null, "<x>").ToHtml());
}
