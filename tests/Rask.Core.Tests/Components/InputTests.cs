#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public class InputTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<input />", Input<string>().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<input id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" type=\"text\" name=\"n\" value=\"v\" placeholder=\"p\" required disabled readonly checked min=\"1\" max=\"10\" step=\"1\" pattern=\"[a-z]&#x2B;\" size=\"20\" maxlength=\"100\" minlength=\"1\" multiple accept=\".png\" capture=\"user\" alt=\"alt\" autocomplete=\"off\" inputmode=\"numeric\" enterkeyhint=\"done\" spellcheck=\"false\" dirname=\"d\" autofocus form=\"f\" formaction=\"/a\" formenctype=\"multipart/form-data\" formmethod=\"post\" formnovalidate formtarget=\"_blank\" list=\"l\" src=\"/s\" width=\"80\" height=\"40\" />",
            Input<string>(InputType.Text, "n", "v", "p", true, true, true, true, "1", "10", "1", "[a-z]+", 20, 100, 1, true, ".png",
                "alt", "off", true, "f", "/a", "multipart/form-data", "post", true, "_blank", "l", "/s", 80, 40,
                InputMode: "numeric", EnterKeyHint: "done", Spellcheck: false, Capture: "user", Dirname: "d",
                Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Theory]
    [InlineData(InputType.Email, "email")]
    [InlineData(InputType.Password, "password")]
    [InlineData(InputType.Tel, "tel")]
    [InlineData(InputType.Url, "url")]
    [InlineData(InputType.Search, "search")]
    [InlineData(InputType.Range, "range")]
    [InlineData(InputType.Color, "color")]
    [InlineData(InputType.File, "file")]
    [InlineData(InputType.Week, "week")]
    [InlineData(InputType.Month, "month")]
    [InlineData(InputType.Hidden, "hidden")]
    [InlineData(InputType.Radio, "radio")]
    [InlineData(InputType.DatetimeLocal, "datetime-local")]
    public void Render_ExplicitType_EmitsTypeAttribute(InputType type, string html) =>
        Assert.Equal($"<input type=\"{html}\" />", Input<string>(type).ToHtml());

    [Fact]
    public void Render_SpellcheckFalse_EmitsEnumeratedValue() =>
        // spellcheck is an enumerated attribute, not a boolean-presence one — false must render explicitly.
        Assert.Equal("<input spellcheck=\"false\" />", Input<string>(Spellcheck: false).ToHtml());

    [Fact]
    public void Render_FileCaptureAndKeyboardHints_EmitInDeclaredOrder() =>
        Assert.Equal(
            "<input type=\"file\" capture=\"environment\" inputmode=\"none\" enterkeyhint=\"send\" dirname=\"d\" />",
            Input<string>(InputType.File, Capture: "environment", InputMode: "none", EnterKeyHint: "send",
                Dirname: "d").ToHtml());

    [Fact]
    public void Render_OnInputOutsideLiveContext_OmitsHandlerAttribute()
    {
        Assert.Equal(
            "<input />",
            Input<string>(OnInput: _ => { }).ToHtml());
    }

    [Fact]
    public void Render_OnInputAndOnChangeInsideLiveContext_EmitSequentialIds()
    {
        var view = new StubComponent(() => Input<string>(OnInput: _ => { }, OnChange: _ => { }));
        Assert.Equal(
            "<input data-rask-on-input=\"h0\" data-rask-on-change=\"h1\" />",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnInputAsyncAndOnChangeAsyncInsideLiveContext_EmitSequentialIds()
    {
        var view = new StubComponent(() => Input<string>(OnInputAsync: async _ => { await Task.Yield(); },
            OnChangeAsync: async _ => { await Task.Yield(); }));
        Assert.Equal(
            "<input data-rask-on-input=\"h0\" data-rask-on-change=\"h1\" />",
            view.RenderAsLiveRoot());
    }
}
