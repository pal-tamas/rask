using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class ScriptTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<script></script>", new Script().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        
        Assert.Equal(
            "<script id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/app.js\" type=\"module\" async defer crossorigin=\"anonymous\" integrity=\"sha384-abc\" nomodule referrerpolicy=\"no-referrer\" charset=\"utf-8\"></script>",
            new Script { Src = "/app.js", Type = "module", Async = true, Defer = true, CrossOrigin = "anonymous", Integrity = "sha384-abc", NoModule = true, ReferrerPolicy = "no-referrer", Charset = "utf-8", Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<script>&lt;x&gt;</script>", new Script { Children = ["<x>"] }.ToHtml());
}
