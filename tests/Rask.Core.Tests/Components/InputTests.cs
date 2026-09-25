#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public partial class InputTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_a_self_closing_tag() =>
        Assert.Equal("<input />", Input.Of<string>().ToHtml());

    [Fact]
    public void A_decimal_binding_emits_step_any()
    {
        // HTML defaults to step="1", so without this the browser's own constraint validation rejects a
        // fractional value and refuses to fire submit — silently, with nothing thrown and no message shown.
        // Asserted as exact markup because `step` has to keep its slot in the attribute order.
        var model = new PriceModel();

        Assert.Equal(
            "<input max=\"10\" pattern=\"p\" type=\"number\" name=\"Price\" value=\"0\" step=\"any\" />",
            Input.Bind(() => model.Price).Max("10").Pattern("p").ToHtml());
    }

    [Fact]
    public void An_int_binding_keeps_the_implicit_whole_number_step()
    {
        // Integral types must NOT get step="any" — there, whole numbers are the constraint you want.
        var model = new PriceModel();

        Assert.DoesNotContain("step=", Input.Bind(() => model.Quantity).ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_explicit_step_wins_over_the_default()
    {
        var model = new PriceModel();

        Assert.Contains("step=\"0.01\"", Input.Bind(() => model.Price).Step("0.01").ToHtml(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "<input type=\"radio\" name=\"Express\" checked />")]
    [InlineData(false, "<input type=\"radio\" name=\"Express\" />")]
    public void A_radio_bound_to_a_bool_writes_checked_rather_than_a_value(bool express, string expected)
    {
        // A radio bound over a bool is asking whether THIS option is the chosen one, which is the same
        // question a checkbox asks — so the model's value is its `checked` state. It used to fall
        // through to the value branch and render `value="True"` with no checked at all: a bound radio
        // that reads correctly in C# and comes out unset in the markup on every frame.
        var model = new ShippingModel { Express = express };

        Assert.Equal(expected, Input.Bind(() => model.Express).Type(InputType.Radio).ToHtml());
    }

    [Fact]
    public void A_radio_bound_to_anything_else_still_carries_the_group_value()
    {
        // Only a bool means "this option". A radio bound over anything else is carrying the group's
        // value, and that has to reach the markup as a value.
        var model = new ShippingModel { Choice = "express" };

        Assert.Equal(
            "<input type=\"radio\" name=\"Choice\" value=\"express\" />",
            Input.Bind(() => model.Choice).Type(InputType.Radio).ToHtml());
    }

    private sealed class PriceModel
    {
        public decimal Price { get; set; }

        public int Quantity { get; set; }
    }

    private sealed class ShippingModel
    {
        public bool Express { get; set; }

        public string Choice { get; set; } = "";
    }

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<input id=\"i\" class=\"c\" style=\"s\" spellcheck=\"false\" data-k=\"v\" autofocus enterkeyhint=\"done\" inputmode=\"numeric\" capture=\"user\" accept=\".png\" alt=\"alt\" autocomplete=\"off\" dirname=\"d\" disabled form=\"f\" formaction=\"/a\" formenctype=\"multipart/form-data\" formmethod=\"post\" formnovalidate formtarget=\"_blank\" height=\"40\" list=\"l\" max=\"10\" maxlength=\"100\" min=\"1\" minlength=\"1\" multiple pattern=\"[a-z]&#x2B;\" placeholder=\"p\" readonly required size=\"20\" src=\"/s\" width=\"80\" type=\"text\" name=\"n\" value=\"v\" checked step=\"1\" />",
            Input.Value("v").Type(InputType.Text).Name("n").Placeholder("p").Required(true).Disabled(true)
                .ReadOnly(true).Checked(true).Min("1").Max("10").Step("1").Pattern("[a-z]+").Size(20)
                .MaxLength(100).MinLength(1).Multiple(true).Accept(".png").Alt("alt").Autocomplete("off")
                .Autofocus(true).Form("f").FormAction("/a").FormEnctype("multipart/form-data").FormMethod("post")
                .FormNoValidate(true).FormTarget("_blank").List("l").Src("/s").Width(80).Height(40)
                .InputMode("numeric").EnterKeyHint("done").Spellcheck(false).Capture("user").DirName("d")
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
    public void An_explicit_type_emits_the_type_attribute(InputType type, string html) =>
        Assert.Equal($"<input type=\"{html}\" />", Input.Of<string>().Type(type).ToHtml());

    [Fact]
    public void It_honours_the_caller_s_aria_role_and_tab_index() =>
        // Global attributes come through Element in the canonical slot order (role, tabindex, aria-*) BEFORE
        // the tag-specific `type` — a caller can wire an accessible name / role onto a bare input.
        Assert.Equal(
            "<input role=\"switch\" tabindex=\"0\" aria-label=\"volume\" type=\"range\" />",
            Input.Of<string>().Type(InputType.Range).Role("switch").TabIndex(0)
                .Aria(new Dictionary<string, string?> { ["label"] = "volume" }).ToHtml());

    [Fact]
    public void Spellcheck_off_emits_the_enumerated_value() =>
        // spellcheck is an enumerated attribute, not a boolean-presence one — false must render explicitly.
        Assert.Equal("<input spellcheck=\"false\" />", Input.Of<string>().Spellcheck(false).ToHtml());

    [Fact]
    public void File_capture_and_the_keyboard_hints_come_in_declared_order() =>
        Assert.Equal(
            "<input enterkeyhint=\"send\" inputmode=\"none\" capture=\"environment\" dirname=\"d\" type=\"file\" />",
            Input.Of<string>().Type(InputType.File).Capture("environment").InputMode("none").EnterKeyHint("send")
                .DirName("d").ToHtml());

    [Fact]
    public void An_input_handler_outside_a_live_context_emits_no_handler_attribute()
    {
        Assert.Equal(
            "<input />",
            Input.Of<string>().OnInput(_ => { }).ToHtml());
    }

    [Fact]
    public void An_input_and_a_change_handler_are_given_sequential_ids()
    {
        var view = new StubComponent(() => Input.Of<string>().OnInput(_ => { }).OnChange(_ => { }));
        Assert.Equal(
            "<input data-rask-on-input=\"h0\" data-rask-on-change=\"h1\" />",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Two_async_handlers_are_given_sequential_ids()
    {
        var view = new StubComponent(() => Input.Of<string>().OnInput(async _ => { await Task.Yield(); })
            .OnChange(async _ => { await Task.Yield(); }));
        Assert.Equal(
            "<input data-rask-on-input=\"h0\" data-rask-on-change=\"h1\" />",
            view.RenderAsLiveRoot());
    }
}
