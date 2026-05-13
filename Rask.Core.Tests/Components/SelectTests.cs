using Rask.Core.Components;
using Rask.Core.Tests.Live;

namespace Rask.Core.Tests.Components;

public class SelectTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<select></select>", new Select().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal(
            "<select id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" name=\"n\" multiple required disabled size=\"5\" form=\"f\" autofocus autocomplete=\"off\"></select>",
            new Select { Name = "n", Multiple = true, Required = true, Disabled = true, Size = 5, Form = "f", Autofocus = true, Autocomplete = "off", Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<select>&lt;x&gt;</select>", new Select { Children = ["<x>"] }.ToHtml());

    [Fact]
    public void Render_OnChangeOutsideLiveContext_OmitsHandlerAttribute() =>
        Assert.Equal(
            "<select></select>",
            new Select { OnChange = _ => { } }.ToHtml());

    [Fact]
    public void Render_OnChangeInsideLiveContext_EmitsDataRaskOnChange()
    {
        var view = new StubComponent(() => new Select { OnChange = _ => { } });
        Assert.Equal(
            "<select data-rask-on-change=\"h0\"></select>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnChangeAsyncInsideLiveContext_EmitsDataRaskOnChange()
    {
        var view = new StubComponent(() => new Select { OnChangeAsync = async _ => { await Task.Yield(); } });
        Assert.Equal(
            "<select data-rask-on-change=\"h0\"></select>",
            view.RenderAsLiveRoot());
    }
}
