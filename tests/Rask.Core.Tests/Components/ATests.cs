#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public class ATests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<a></a>", A().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<a id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" href=\"/foo\" target=\"_blank\" rel=\"noopener\" download=\"file.zip\" hreflang=\"en\" type=\"text/html\" referrerpolicy=\"no-referrer\" ping=\"https://ping\"></a>",
            A("/foo", "_blank", "noopener", "file.zip", "en", "text/html", "no-referrer", "https://ping", Id: "i",
                Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() => Assert.Equal("<a>&lt;x&gt;</a>", A()["<x>"].ToHtml());

    [Fact]
    public void Render_OnClickOutsideLiveContext_OmitsHandlerAttribute() =>
        Assert.Equal("<a></a>", A(OnClick: () => { }).ToHtml());

    [Fact]
    public void Render_OnClickInsideLiveContext_EmitsDataRaskOnClick()
    {
        var view = new StubComponent(() => A(OnClick: () => { })["go"]);
        Assert.Equal("<a data-rask-on-click=\"h0\">go</a>", view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnClickAsyncInsideLiveContext_EmitsDataRaskOnClick()
    {
        var view = new StubComponent(() => A(OnClickAsync: async () => { await Task.Yield(); })["go"]);
        Assert.Equal("<a data-rask-on-click=\"h0\">go</a>", view.RenderAsLiveRoot());
    }
}
