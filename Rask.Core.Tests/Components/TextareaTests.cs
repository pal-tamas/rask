using Rask.Core.Components;
using Rask.Core.Tests.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public class TextareaTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<textarea></textarea>", Textarea().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal(
            "<textarea id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" name=\"n\" rows=\"4\" cols=\"80\" placeholder=\"p\" required disabled readonly maxlength=\"100\" minlength=\"1\" wrap=\"soft\" autofocus autocomplete=\"off\" form=\"f\" dirname=\"d\"></textarea>",
            Textarea(Name: "n", Rows: 4, Cols: 80, Placeholder: "p", Required: true, Disabled: true, ReadOnly: true, MaxLength: 100, MinLength: 1, Wrap: "soft", Autofocus: true, Autocomplete: "off", Form: "f", Dirname: "d", Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<textarea>&lt;x&gt;</textarea>", Textarea()["<x>"].ToHtml());

    [Fact]
    public void Render_OnInputOutsideLiveContext_OmitsHandlerAttribute() =>
        Assert.Equal(
            "<textarea></textarea>",
            Textarea(OnInput: _ => { }).ToHtml());

    [Fact]
    public void Render_OnInputAndOnChangeInsideLiveContext_EmitSequentialIds()
    {
        var view = new StubComponent(() => Textarea(OnInput: _ => { }, OnChange: _ => { }));
        Assert.Equal(
            "<textarea data-rask-on-input=\"h0\" data-rask-on-change=\"h1\"></textarea>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnInputAsyncAndOnChangeAsyncInsideLiveContext_EmitSequentialIds()
    {
        var view = new StubComponent(() => Textarea(OnInputAsync: async _ => { await Task.Yield(); }, OnChangeAsync: async _ => { await Task.Yield(); }));
        Assert.Equal(
            "<textarea data-rask-on-input=\"h0\" data-rask-on-change=\"h1\"></textarea>",
            view.RenderAsLiveRoot());
    }
}
