using Rask.Core.Components;
using Rask.Core.Tests.Live;

namespace Rask.Core.Tests.Components;

public class ATests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<a></a>", new A(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new A.Props(
            "/foo",
            "_blank",
            "noopener",
            "file.zip",
            "en",
            "text/html",
            "no-referrer",
            "https://ping",
            Id: "i",
            Class: "c",
            Style: "s",
            Data: new Dictionary<string, string?> { ["k"] = "v" });

        Assert.Equal(
            "<a id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" href=\"/foo\" target=\"_blank\" rel=\"noopener\" download=\"file.zip\" hreflang=\"en\" type=\"text/html\" referrerpolicy=\"no-referrer\" ping=\"https://ping\"></a>",
            new A(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() => Assert.Equal("<a>&lt;x&gt;</a>", new A(null, "<x>").ToHtml());

    [Fact]
    public void Render_OnClickOutsideLiveContext_OmitsHandlerAttribute() =>
        Assert.Equal("<a></a>", new A(new A.Props(OnClick: () => { })).ToHtml());

    [Fact]
    public void Render_OnClickInsideLiveContext_EmitsDataRaskOnClick()
    {
        var view = new StubComponent(() => new A(new A.Props(OnClick: () => { }), "go"));
        Assert.Equal("<a data-rask-on-click=\"h0\">go</a>", view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnClickAsyncInsideLiveContext_EmitsDataRaskOnClick()
    {
        var view = new StubComponent(() => new A(
            new A.Props(OnClickAsync: async () => { await Task.Yield(); }),
            "go"));
        Assert.Equal("<a data-rask-on-click=\"h0\">go</a>", view.RenderAsLiveRoot());
    }
}
