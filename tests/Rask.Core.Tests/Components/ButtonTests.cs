#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public partial class ButtonTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_an_empty_button() =>
        Assert.Equal("<button></button>", Button.ToHtml());

    [Fact]
    public void A_true_disabled_emits_a_bare_disabled_attribute()
    {
        Assert.Equal(
            "<button disabled></button>",
            Button.Disabled(true).ToHtml());
    }

    [Fact]
    public void A_false_disabled_omits_the_attribute()
    {
        Assert.Equal(
            "<button></button>",
            Button.Disabled(false).ToHtml());
    }

    [Fact]
    public void A_set_type_emits_the_type_attribute()
    {
        Assert.Equal(
            "<button type=\"submit\"></button>",
            Button.Type("submit").ToHtml());
    }

    [Fact]
    public void Name_and_value_are_both_emitted_quoted()
    {
        Assert.Equal(
            "<button name=\"action\" value=\"save\"></button>",
            Button.Name("action").Value("save").ToHtml());
    }

    // The form-override set, plus the popover pair and autofocus (#694). Input has had all six form-*
    // attributes since it was written, so until now a submit button could override the form's action
    // spelled as <input type="submit"> but not as <button> — an inconsistency, not a decision.
    [Fact]
    public void The_form_overrides_emit_every_spec_attribute() =>
        Assert.Equal(
            "<button type=\"submit\" form=\"checkout\" formaction=\"/pay\" "
            + "formenctype=\"multipart/form-data\" formmethod=\"post\" formnovalidate "
            + "formtarget=\"_blank\"></button>",
            Button
                .Type("submit")
                .Form("checkout")
                .FormAction("/pay")
                .FormEnctype("multipart/form-data")
                .FormMethod("post")
                .FormNovalidate(true)
                .FormTarget("_blank")
                .ToHtml());

    [Fact]
    public void The_popover_target_pairs_with_the_global_popover_attribute() =>
        // The other half of Element.Popover: the browser opens it, handles light-dismiss, the top layer
        // and focus, with no JavaScript on either side.
        Assert.Equal(
            "<button popovertarget=\"menu\" popovertargetaction=\"toggle\"></button>",
            Button.PopoverTarget("menu").PopoverTargetAction("toggle").ToHtml());

    [Fact]
    public void Bare_booleans_emit_presence_only()
    {
        Assert.Equal("<button autofocus></button>", Button.Autofocus(true).ToHtml());
        Assert.Equal("<button></button>", Button.Autofocus(false).ToHtml());
        Assert.Equal("<button></button>", Button.FormNovalidate(false).ToHtml());
    }

    [Fact]
    public void Setting_every_prop_emits_the_base_then_the_derived_attributes_in_order()
    {
        Assert.Equal(
            "<button id=\"go\" class=\"action\" style=\"color:red\" data-test-id=\"primary\" type=\"submit\" disabled name=\"action\" value=\"save\"></button>",
            Button
                .Type("submit")
                .Disabled(true)
                .Name("action")
                .Value("save")
                .Id("go")
                .Class("action")
                .Style("color:red")
                .Data(new Dictionary<string, string?> { ["test-id"] = "primary" }).ToHtml());
    }

    [Fact]
    public void Accessibility_props_precede_the_tag_specific_attributes()
    {
        Assert.Equal(
            "<button data-test-id=\"x\" role=\"button\" tabindex=\"0\" aria-pressed=\"true\" type=\"submit\" disabled></button>",
            Button
                .Type("submit")
                .Disabled(true)
                .Data(new Dictionary<string, string?> { ["test-id"] = "x" })
                .Role("button")
                .TabIndex(0)
                .Aria(new Dictionary<string, string?> { ["pressed"] = "true" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text()
    {
        Assert.Equal(
            "<button>&lt;click&gt;</button>",
            Button["<click>"].ToHtml());
    }

    [Fact]
    public void A_raw_child_renders_verbatim()
    {
        Assert.Equal(
            "<button><i>!</i></button>",
            Button[Raw.Value("<i>!</i>")].ToHtml());
    }

    [Fact]
    public void Children_passed_as_params_render_in_order()
    {
        Assert.Equal(
            "<button>a<b></button>",
            Button["a", Raw.Value("<b>")].ToHtml());
    }

    [Fact]
    public void Children_passed_as_an_enumerable_render_in_order()
    {
        var children = new List<Component> { "a", Raw.Value("<b>") };

        Assert.Equal(
            "<button>a<b></button>",
            Button[children].ToHtml());
    }

    [Fact]
    public void OnClick_outside_a_live_context_emits_no_handler_attribute() =>
        Assert.Equal("<button></button>", Button.OnClick(() => { }).ToHtml());

    [Fact]
    public void OnClick_inside_a_live_context_emits_the_click_handler_id()
    {
        var view = new StubComponent(() => Button.OnClick(() => { })["x"]);

        Assert.Equal(
            "<button data-rask-on-click=\"h0\">x</button>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void An_async_OnClick_inside_a_live_context_emits_the_click_handler_id()
    {
        var view = new StubComponent(() => Button.OnClick(async () => { await Task.Yield(); })["x"]);

        Assert.Equal(
            "<button data-rask-on-click=\"h0\">x</button>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void The_form_overrides_emit_after_the_button_attributes() =>
        Assert.Equal(
            "<button type=\"submit\" name=\"n\" value=\"v\" autofocus form=\"f\" formaction=\"/save\" "
            + "formenctype=\"multipart/form-data\" formmethod=\"post\" formnovalidate formtarget=\"_blank\">"
            + "Save</button>",
            Button
                .Type("submit")
                .Name("n")
                .Value("v")
                .Autofocus(true)
                .Form("f")
                .FormAction("/save")
                .FormEnctype("multipart/form-data")
                .FormMethod("post")
                .FormNovalidate(true)
                .FormTarget("_blank")["Save"].ToHtml());

    // formaction is a navigation target, so it goes through the same sanitiser as href/src.
    [Fact]
    public void A_javascript_formaction_is_sanitised() =>
        Assert.DoesNotContain("javascript:", Button.FormAction("javascript:alert(1)").ToHtml(),
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void The_popover_target_emits_both_halves() =>
        Assert.Equal(
            "<button popovertarget=\"menu\" popovertargetaction=\"toggle\">Open</button>",
            Button.PopoverTarget("menu").PopoverTargetAction("toggle")["Open"].ToHtml());

    [Fact]
    public void The_command_emits_both_halves() =>
        // command/commandfor generalise popovertarget past popovers — this drives a <dialog> with no
        // script on either side.
        Assert.Equal(
            "<button command=\"show-modal\" commandfor=\"edit\">Edit</button>",
            Button.Command("show-modal").CommandFor("edit")["Edit"].ToHtml());

    [Fact]
    public void A_custom_command_is_emitted_verbatim() =>
        // A `--name` command dispatches a CommandEvent rather than invoking a built-in action; the
        // leading dashes are part of the value and must survive.
        Assert.Equal(
            "<button command=\"--spin\" commandfor=\"w\">Go</button>",
            Button.Command("--spin").CommandFor("w")["Go"].ToHtml());

    [Fact]
    public void The_command_emits_after_the_popover_pair() =>
        // Appended after popovertarget/popovertargetaction, not inserted among them — the order is the
        // declaration order, and inserting would have shifted every factory parameter below.
        Assert.Equal(
            "<button popovertarget=\"m\" popovertargetaction=\"toggle\" command=\"close\" "
            + "commandfor=\"d\">x</button>",
            Button
                .CommandFor("d")
                .Command("close")
                .PopoverTargetAction("toggle")
                .PopoverTarget("m")["x"].ToHtml());
}
