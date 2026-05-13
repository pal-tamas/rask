using Rask.Core.Components;
using Rask.Core.Tests.Live;

namespace Rask.Core.Tests.Components;

public class FormTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<form></form>", new Form().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal(
            "<form id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" enctype=\"multipart/form-data\" target=\"_blank\" accept-charset=\"utf-8\" autocomplete=\"off\" novalidate name=\"n\"></form>",
            new Form { Enctype = "multipart/form-data", Target = "_blank", AcceptCharset = "utf-8", Autocomplete = "off", Novalidate = true, Name = "n", Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<form>&lt;x&gt;</form>", new Form { Children = ["<x>"] }.ToHtml());

    [Fact]
    public void Render_OnSubmitOutsideLiveContext_OmitsHandlerAttribute() =>
        Assert.Equal(
            "<form></form>",
            new Form { OnSubmit = _ => { } }.ToHtml());

    [Fact]
    public void Render_OnSubmitInsideLiveContext_EmitsDataRaskOnSubmit()
    {
        var view = new StubComponent(() => new Form { OnSubmit = _ => { } });
        Assert.Equal(
            "<form data-rask-on-submit=\"h0\"></form>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnSubmitAsyncInsideLiveContext_EmitsDataRaskOnSubmit()
    {
        var view = new StubComponent(() => new Form { OnSubmitAsync = async _ => { await Task.Yield(); } });
        Assert.Equal(
            "<form data-rask-on-submit=\"h0\"></form>",
            view.RenderAsLiveRoot());
    }
}
