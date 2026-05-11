using Rask.Core.Components;
using Rask.Core.Tests.Live;

namespace Rask.Core.Tests.Components;

public class FormTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<form></form>", new Form(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Form.Props(
            "multipart/form-data",
            "_blank", "utf-8", "off",
            true, "n",
            Id: "i", Class: "c", Style: "s",
            Data: new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal(
            "<form id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" enctype=\"multipart/form-data\" target=\"_blank\" accept-charset=\"utf-8\" autocomplete=\"off\" novalidate name=\"n\"></form>",
            new Form(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<form>&lt;x&gt;</form>", new Form(null, "<x>").ToHtml());

    [Fact]
    public void Render_OnSubmitOutsideLiveContext_OmitsHandlerAttribute() =>
        Assert.Equal(
            "<form></form>",
            new Form(new Form.Props(OnSubmit: _ => { })).ToHtml());

    [Fact]
    public void Render_OnSubmitInsideLiveContext_EmitsDataRaskOnSubmit()
    {
        var view = new StubComponent(() => new Form(new Form.Props(OnSubmit: _ => { })));
        Assert.Equal(
            "<form data-rask-on-submit=\"h0\"></form>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnSubmitAsyncInsideLiveContext_EmitsDataRaskOnSubmit()
    {
        var view = new StubComponent(() => new Form(
            new Form.Props(OnSubmitAsync: async _ => { await Task.Yield(); })));
        Assert.Equal(
            "<form data-rask-on-submit=\"h0\"></form>",
            view.RenderAsLiveRoot());
    }
}
