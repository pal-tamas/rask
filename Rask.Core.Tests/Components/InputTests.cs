using Rask.Core.Components;
using Rask.Core.Tests.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public class InputTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<input />", Input().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal(
            "<input id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" type=\"text\" name=\"n\" value=\"v\" placeholder=\"p\" required disabled readonly checked min=\"1\" max=\"10\" step=\"1\" pattern=\"[a-z]&#x2B;\" size=\"20\" maxlength=\"100\" minlength=\"1\" multiple accept=\".png\" alt=\"alt\" autocomplete=\"off\" autofocus form=\"f\" formaction=\"/a\" formenctype=\"multipart/form-data\" formmethod=\"post\" formnovalidate formtarget=\"_blank\" list=\"l\" src=\"/s\" width=\"80\" height=\"40\" />",
            Input(Type: "text", Name: "n", Value: "v", Placeholder: "p", Required: true, Disabled: true, ReadOnly: true, Checked: true, Min: "1", Max: "10", Step: "1", Pattern: "[a-z]+", Size: 20, MaxLength: 100, MinLength: 1, Multiple: true, Accept: ".png", Alt: "alt", Autocomplete: "off", Autofocus: true, Form: "f", FormAction: "/a", FormEnctype: "multipart/form-data", FormMethod: "post", FormNovalidate: true, FormTarget: "_blank", List: "l", Src: "/s", Width: 80, Height: 40, Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_OnInputOutsideLiveContext_OmitsHandlerAttribute()
    {
        Assert.Equal(
            "<input />",
            Input(OnInput: _ => { }).ToHtml());
    }

    [Fact]
    public void Render_OnInputAndOnChangeInsideLiveContext_EmitSequentialIds()
    {
        var view = new StubComponent(() => Input(OnInput: _ => { }, OnChange: _ => { }));
        Assert.Equal(
            "<input data-rask-on-input=\"h0\" data-rask-on-change=\"h1\" />",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnInputAsyncAndOnChangeAsyncInsideLiveContext_EmitSequentialIds()
    {
        var view = new StubComponent(() => Input(OnInputAsync: async _ => { await Task.Yield(); }, OnChangeAsync: async _ => { await Task.Yield(); }));
        Assert.Equal(
            "<input data-rask-on-input=\"h0\" data-rask-on-change=\"h1\" />",
            view.RenderAsLiveRoot());
    }
}
