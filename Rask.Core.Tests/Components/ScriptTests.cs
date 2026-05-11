using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class ScriptTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<script></script>", new Script(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Script.Props(
            "/app.js",
            "module",
            true,
            true,
            "anonymous",
            "sha384-abc",
            true,
            "no-referrer",
            "utf-8",
            "i",
            "c",
            "s",
            new Dictionary<string, string?> { ["k"] = "v" });

        Assert.Equal(
            "<script id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/app.js\" type=\"module\" async defer crossorigin=\"anonymous\" integrity=\"sha384-abc\" nomodule referrerpolicy=\"no-referrer\" charset=\"utf-8\"></script>",
            new Script(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<script>&lt;x&gt;</script>", new Script(null, "<x>").ToHtml());
}
