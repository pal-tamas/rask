using Rask.Core.Components;
using Rask.Core.Tests.Live;

namespace Rask.Core.Tests.Components;

public class TextareaTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<textarea></textarea>", new Textarea(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Textarea.Props(
            "n", 4, 80, "p",
            true, true, true,
            100, 1, "soft",
            true, "off", "f", "d",
            Id: "i", Class: "c", Style: "s",
            Data: new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal(
            "<textarea id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" name=\"n\" rows=\"4\" cols=\"80\" placeholder=\"p\" required disabled readonly maxlength=\"100\" minlength=\"1\" wrap=\"soft\" autofocus autocomplete=\"off\" form=\"f\" dirname=\"d\"></textarea>",
            new Textarea(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<textarea>&lt;x&gt;</textarea>", new Textarea(null, "<x>").ToHtml());

    [Fact]
    public void Render_OnInputOutsideLiveContext_OmitsHandlerAttribute() =>
        Assert.Equal(
            "<textarea></textarea>",
            new Textarea(new Textarea.Props(OnInput: _ => { })).ToHtml());

    [Fact]
    public void Render_OnInputAndOnChangeInsideLiveContext_EmitSequentialIds()
    {
        var view = new StubComponent(() => new Textarea(new Textarea.Props(
            OnInput: _ => { },
            OnChange: _ => { })));
        Assert.Equal(
            "<textarea data-rask-on-input=\"h0\" data-rask-on-change=\"h1\"></textarea>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnInputAsyncAndOnChangeAsyncInsideLiveContext_EmitSequentialIds()
    {
        var view = new StubComponent(() => new Textarea(new Textarea.Props(
            OnInputAsync: async _ => { await Task.Yield(); },
            OnChangeAsync: async _ => { await Task.Yield(); })));
        Assert.Equal(
            "<textarea data-rask-on-input=\"h0\" data-rask-on-change=\"h1\"></textarea>",
            view.RenderAsLiveRoot());
    }
}
