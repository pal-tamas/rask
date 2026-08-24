#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Html.Tests.Components;

public partial class InputTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<input />", Input.Of<string>().ToHtml());

    [Fact]
    public void Render_DecimalBinding_EmitsStepAnyBetweenMaxAndPattern()
    {
        // HTML defaults to step="1", so without this the browser's own constraint validation rejects a
        // fractional value and refuses to fire submit — silently, with nothing thrown and no message shown.
        // Asserted as exact markup because `step` has to keep its slot in the attribute order.
        var model = new PriceModel();

        Assert.Equal(
            "<input type=\"number\" name=\"Price\" value=\"0\" max=\"10\" step=\"any\" pattern=\"p\" />",
            Input.Bind(() => model.Price).Max("10").Pattern("p").ToHtml());
    }

    [Fact]
    public void Render_IntBinding_KeepsTheImplicitWholeNumberStep()
    {
        // Integral types must NOT get step="any" — there, whole numbers are the constraint you want.
        var model = new PriceModel();

        Assert.DoesNotContain("step=", Input.Bind(() => model.Quantity).ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ExplicitStep_WinsOverTheDefault()
    {
        var model = new PriceModel();

        Assert.Contains("step=\"0.01\"", Input.Bind(() => model.Price).Step("0.01").ToHtml(), StringComparison.Ordinal);
    }

    private sealed class PriceModel
    {
        public decimal Price { get; set; }

        public int Quantity { get; set; }
    }

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<input id=\"i\" class=\"c\" style=\"s\" spellcheck=\"false\" data-k=\"v\" type=\"text\" name=\"n\" value=\"v\" placeholder=\"p\" required disabled readonly checked min=\"1\" max=\"10\" step=\"1\" pattern=\"[a-z]&#x2B;\" size=\"20\" maxlength=\"100\" minlength=\"1\" multiple accept=\".png\" capture=\"user\" alt=\"alt\" autocomplete=\"off\" inputmode=\"numeric\" enterkeyhint=\"done\" dirname=\"d\" autofocus form=\"f\" formaction=\"/a\" formenctype=\"multipart/form-data\" formmethod=\"post\" formnovalidate formtarget=\"_blank\" list=\"l\" src=\"/s\" width=\"80\" height=\"40\" />",
            Input.Value("v").Type(InputType.Text).Name("n").Placeholder("p").Required(true).Disabled(true)
                .ReadOnly(true).Checked(true).Min("1").Max("10").Step("1").Pattern("[a-z]+").Size(20)
                .MaxLength(100).MinLength(1).Multiple(true).Accept(".png").Alt("alt").Autocomplete("off")
                .Autofocus(true).Form("f").FormAction("/a").FormEnctype("multipart/form-data").FormMethod("post")
                .FormNovalidate(true).FormTarget("_blank").List("l").Src("/s").Width(80).Height(40)
                .InputMode("numeric").EnterKeyHint("done").Spellcheck(false).Capture("user").Dirname("d")
                .Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
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
        Assert.Equal($"<input type=\"{html}\" />", Input.Of<string>().Type(type).ToHtml());

    [Fact]
    public void Render_HonorsCallerAriaRoleAndTabIndex() =>
        // Global attributes come through Element in the canonical slot order (role, tabindex, aria-*) BEFORE
        // the tag-specific `type` — a caller can wire an accessible name / role onto a bare input.
        Assert.Equal(
            "<input role=\"switch\" tabindex=\"0\" aria-label=\"volume\" type=\"range\" />",
            Input.Of<string>().Type(InputType.Range).Role("switch").TabIndex(0)
                .Aria(new Dictionary<string, string?> { ["label"] = "volume" }).ToHtml());

    [Fact]
    public void Render_SpellcheckFalse_EmitsEnumeratedValue() =>
        // spellcheck is an enumerated attribute, not a boolean-presence one — false must render explicitly.
        Assert.Equal("<input spellcheck=\"false\" />", Input.Of<string>().Spellcheck(false).ToHtml());

    [Fact]
    public void Render_FileCaptureAndKeyboardHints_EmitInDeclaredOrder() =>
        Assert.Equal(
            "<input type=\"file\" capture=\"environment\" inputmode=\"none\" enterkeyhint=\"send\" dirname=\"d\" />",
            Input.Of<string>().Type(InputType.File).Capture("environment").InputMode("none").EnterKeyHint("send")
                .Dirname("d").ToHtml());

    [Fact]
    public void Render_OnInputOutsideLiveContext_OmitsHandlerAttribute()
    {
        Assert.Equal(
            "<input />",
            Input.Of<string>().OnInput(_ => { }).ToHtml());
    }

    [Fact]
    public void Render_OnInputAndOnChangeInsideLiveContext_EmitSequentialIds()
    {
        var view = new StubComponent(() => Input.Of<string>().OnInput(_ => { }).OnChange(_ => { }));
        Assert.Equal(
            "<input data-rask-on-input=\"h0\" data-rask-on-change=\"h1\" />",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnInputAsyncAndOnChangeAsyncInsideLiveContext_EmitSequentialIds()
    {
        var view = new StubComponent(() => Input.Of<string>().OnInputAsync(async _ => { await Task.Yield(); })
            .OnChangeAsync(async _ => { await Task.Yield(); }));
        Assert.Equal(
            "<input data-rask-on-input=\"h0\" data-rask-on-change=\"h1\" />",
            view.RenderAsLiveRoot());
    }
}
