#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public partial class TextareaTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<textarea></textarea>", Textarea.Value<string>(null).ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<textarea id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" name=\"n\" rows=\"4\" cols=\"80\" placeholder=\"p\" required disabled readonly maxlength=\"100\" minlength=\"1\" wrap=\"soft\" autofocus autocomplete=\"off\" form=\"f\" dirname=\"d\"></textarea>",
            Textarea.Value<string>(null)
                .Name("n")
                .Rows(4)
                .Cols(80)
                .Placeholder("p")
                .Required(true)
                .Disabled(true)
                .ReadOnly(true)
                .MaxLength(100)
                .MinLength(1)
                .Wrap("soft")
                .Autofocus(true)
                .Autocomplete("off")
                .Form("f")
                .Dirname("d")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" })
                .ToHtml());
    }

    [Fact]
    public void A_text_child_is_html_encoded() =>
        Assert.Equal("<textarea>&lt;x&gt;</textarea>", Textarea.Of<string>()["<x>"].ToHtml());

    [Fact]
    public void An_input_handler_outside_a_live_context_emits_no_handler_attribute() =>
        Assert.Equal(
            "<textarea></textarea>",
            Textarea.Value<string>(null).OnInput(_ => { }).ToHtml());

    [Fact]
    public void An_input_and_a_change_handler_are_given_sequential_ids()
    {
        var view = new StubComponent(() => Textarea.Value<string>(null).OnInput(_ => { }).OnChange(_ => { }));
        Assert.Equal(
            "<textarea data-rask-on-input=\"h0\" data-rask-on-change=\"h1\"></textarea>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Two_async_handlers_are_given_sequential_ids()
    {
        var view = new StubComponent(() => Textarea.Value<string>(null)
            .OnInput(async _ => { await Task.Yield(); })
            .OnChange(async _ => { await Task.Yield(); }));
        Assert.Equal(
            "<textarea data-rask-on-input=\"h0\" data-rask-on-change=\"h1\"></textarea>",
            view.RenderAsLiveRoot());
    }
}
