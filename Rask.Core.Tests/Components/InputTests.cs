using Rask.Core.Components;
using Rask.Core.Tests.Live;

namespace Rask.Core.Tests.Components;

public class InputTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<input />", new Input().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Input.Props(
            "text", "n", "v", "p",
            true, true, true, true,
            "1", "10", "1", "[a-z]+",
            20, 100, 1, true,
            ".png", "alt", "off", true,
            "f", "/a", "multipart/form-data",
            "post", true, "_blank",
            "l", "/s", 80, 40,
            Id: "i", Class: "c", Style: "s",
            Data: new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal(
            "<input id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" type=\"text\" name=\"n\" value=\"v\" placeholder=\"p\" required disabled readonly checked min=\"1\" max=\"10\" step=\"1\" pattern=\"[a-z]&#x2B;\" size=\"20\" maxlength=\"100\" minlength=\"1\" multiple accept=\".png\" alt=\"alt\" autocomplete=\"off\" autofocus form=\"f\" formaction=\"/a\" formenctype=\"multipart/form-data\" formmethod=\"post\" formnovalidate formtarget=\"_blank\" list=\"l\" src=\"/s\" width=\"80\" height=\"40\" />",
            new Input(props).ToHtml());
    }

    [Fact]
    public void Render_OnInputOutsideLiveContext_OmitsHandlerAttribute()
    {
        Assert.Equal(
            "<input />",
            new Input(new Input.Props(OnInput: _ => { })).ToHtml());
    }

    [Fact]
    public void Render_OnInputAndOnChangeInsideLiveContext_EmitSequentialIds()
    {
        var view = new StubComponent(() => new Input(new Input.Props(
            OnInput: _ => { },
            OnChange: _ => { })));
        Assert.Equal(
            "<input data-rask-on-input=\"h0\" data-rask-on-change=\"h1\" />",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnInputAsyncAndOnChangeAsyncInsideLiveContext_EmitSequentialIds()
    {
        var view = new StubComponent(() => new Input(new Input.Props(
            OnInputAsync: async _ => { await Task.Yield(); },
            OnChangeAsync: async _ => { await Task.Yield(); })));
        Assert.Equal(
            "<input data-rask-on-input=\"h0\" data-rask-on-change=\"h1\" />",
            view.RenderAsLiveRoot());
    }
}
