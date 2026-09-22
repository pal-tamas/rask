using System.Text.Json;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public partial class SelectTests : global::Rask.Core.RaskMarkup
{
    // Preselection (MarkSelected) marks the <option> whose value matches the bound model
    // value at serialize time (Select.EnterChildrenScope), so it works whether options are
    // supplied via the `Children:` factory argument OR the `[...]` indexer. The indexer
    // cases below pin the latter (it used to silently fail — the indexer overwrote the
    // factory-time preselection).

    [Fact]
    public void A_bound_select_with_indexer_children_preselects_a_matching_non_first_option()
    {
        // The exact shape that used to break: idiomatic indexer syntax, bound value matching
        // a non-first option. Factory-time MarkSelected never saw these children.
        var model = new ColorPicker { Color = "red" };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Color)[Option.Value(""), Option.Value("red"), Option.Value("blue")]
        ]);

        var html = view.RenderAsLiveRoot();

        Assert.Contains("<option value=\"red\" selected>", html);
        Assert.DoesNotContain("<option value=\"\" selected>", html);
        Assert.DoesNotContain("<option value=\"blue\" selected>", html);
    }

    [Fact]
    public void A_bound_select_with_indexer_children_and_a_null_value_preselects_the_empty_option()
    {
        var model = new ColorPicker { Color = null };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Color)[Option.Value(""), Option.Value("red")]
        ]);

        var html = view.RenderAsLiveRoot();

        Assert.Contains("<option value=\"\" selected>", html);
        Assert.DoesNotContain("<option value=\"red\" selected>", html);
    }

    [Fact]
    public async Task An_empty_change_sets_a_bound_nullable_string_to_null()
    {
        // `string?` is nullable per the C# NRT annotation; BindingHelpers reads it via
        // NullabilityInfoContext and treats empty input as null — matching Nullable<T>
        // value-type behavior. A non-nullable `string` property would set "" instead
        // (see Empty_input_sets_a_non_nullable_string_to_an_empty_string in FormBindingTests).
        var model = new ColorPicker { Color = "red" };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Color)[Option.Value(""), Option.Value("red")]
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = Markup.Attr(html, "data-rask-on-change");
        Assert.NotNull(changeId);

        using var doc = JsonDocument.Parse("{\"value\":\"\"}");
        var ok = await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.True(ok);
        Assert.Null(model.Color);
    }

    [Fact]
    public async Task A_valid_change_sets_a_bound_nullable_int_to_the_typed_value()
    {
        var model = new ChoiceModel { Choice = null };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Choice)[Option.Value(""), Option.Value("5"), Option.Value("10")]
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = Markup.Attr(html, "data-rask-on-change");
        using var doc = JsonDocument.Parse("{\"value\":\"5\"}");
        var ok = await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.True(ok);
        Assert.Equal(5, model.Choice);
    }

    [Fact]
    public async Task An_empty_change_sets_a_bound_nullable_int_to_null()
    {
        var model = new ChoiceModel { Choice = 5 };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Choice)[Option.Value(""), Option.Value("5")]
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = Markup.Attr(html, "data-rask-on-change");
        using var doc = JsonDocument.Parse("{\"value\":\"\"}");
        var ok = await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.True(ok);
        Assert.Null(model.Choice);
    }

    [Fact]
    public async Task A_valid_change_parses_a_bound_nullable_enum()
    {
        var model = new StatusModel { Status = null };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Status)[Option.Value(""), Option.Value("Active"), Option.Value("Inactive")]
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = Markup.Attr(html, "data-rask-on-change");
        using var doc = JsonDocument.Parse("{\"value\":\"Active\"}");
        var ok = await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.True(ok);
        Assert.Equal(SelectStatus.Active, model.Status);
    }

    [Fact]
    public async Task An_empty_change_sets_a_bound_nullable_enum_to_null()
    {
        var model = new StatusModel { Status = SelectStatus.Active };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Status)[Option.Value(""), Option.Value("Active")]
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = Markup.Attr(html, "data-rask-on-change");
        using var doc = JsonDocument.Parse("{\"value\":\"\"}");
        var ok = await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.True(ok);
        Assert.Null(model.Status);
    }

    [Fact]
    public void An_option_without_a_value_attribute_is_not_preselected_for_a_null_bound_value()
    {
        // Option { Value = null } omits the `value` attribute (Option.cs:15). HTML treats
        // such an option as having its text content as the submitted value, so server-
        // side preselection would mismatch the browser's POST. The Option(Value: "")
        // convention is the contract; this test pins that an attribute-less option does
        // NOT match a null bound value.
        var model = new ColorPicker { Color = null };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Color)[Option["placeholder"], Option.Value("red")]
        ]);

        var html = view.RenderAsLiveRoot();

        Assert.DoesNotContain("selected", html);
    }

    [Fact]
    public void A_marked_option_keeps_its_reconciliation_key()
    {
        // Marking an option selected must preserve its Key. Dropping it shifts the selected option's key on
        // every render (the marked one loses its key while the previously-marked one regains it), so keyed
        // reconciliation mismatches and the browser's live `selected` IDL property is never synced — the
        // <select> visually snaps back to the old value even though the `selected` attribute is correct.
        var model = new ColorPicker { Color = "red" };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Color)[
                Option.Value("").Key("e")["none"],
                Option.Value("red").Key("r")["red"],
                Option.Value("blue").Key("b")["blue"]
            ]
        ]);

        var html = view.RenderAsLiveRoot();

        Assert.Contains("data-rask-key=\"r\" value=\"red\" selected", html);
        // The unselected keyed siblings still carry their keys too.
        Assert.Contains("data-rask-key=\"e\"", html);
        Assert.Contains("data-rask-key=\"b\"", html);
    }

    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<select></select>", Select.Of<string>().ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<select id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" name=\"n\" multiple required disabled size=\"5\" form=\"f\" autofocus autocomplete=\"off\"></select>",
            Select.Of<string>().Name("n").Multiple(true).Required(true).Disabled(true).Size(5).Form("f")
                .Autofocus(true).Autocomplete("off").Id("i").Class("c").Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<select>&lt;x&gt;</select>", Select.Of<string>()["<x>"].ToHtml());

    [Fact]
    public void OnChange_outside_a_live_context_emits_no_handler_attribute() =>
        Assert.Equal(
            "<select></select>",
            Select.Of<string>().OnChange(_ => { }).ToHtml());

    [Fact]
    public void OnChange_inside_a_live_context_emits_the_change_handler_id()
    {
        var view = new StubComponent(() => Select.Of<string>().OnChange(_ => { }));

        Assert.Equal(
            "<select data-rask-on-change=\"h0\"></select>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void An_async_OnChange_inside_a_live_context_emits_the_change_handler_id()
    {
        var view = new StubComponent(() => Select.Of<string>().OnChange(async _ => { await Task.Yield(); }));

        Assert.Equal(
            "<select data-rask-on-change=\"h0\"></select>",
            view.RenderAsLiveRoot());
    }

    // #595 — a <select multiple> reports its whole selection through the frame's `values` array.
    // `select.value` is only the FIRST selected option (the DOM has no multi-value `value`), so the model
    // used to converge on one option out of however many the user picked, from a report that was the
    // wrong shape rather than merely late.

    [Fact]
    public async Task A_bound_multi_select_change_binds_every_reported_option()
    {
        var model = new TagsModel { Tags = [] };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Tags).Multiple(true)[Option.Value("a"), Option.Value("b"), Option.Value("c")]
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = Markup.Attr(html, "data-rask-on-change");
        using var doc = JsonDocument.Parse("{\"value\":\"a\",\"values\":[\"a\",\"c\"]}");
        var ok = await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.True(ok);
        Assert.Equal(["a", "c"], model.Tags);
    }

    [Fact]
    public async Task A_bound_multi_select_change_replaces_rather_than_merges()
    {
        // Set, never merge: every change frame carries the absolute selection, so a replace re-syncs the
        // model even when an intermediate render was coalesced. A membership edit could not.
        var model = new TagsModel { Tags = ["a", "b"] };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Tags).Multiple(true)[Option.Value("a"), Option.Value("b"), Option.Value("c")]
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = Markup.Attr(html, "data-rask-on-change");
        using var doc = JsonDocument.Parse("{\"value\":\"c\",\"values\":[\"c\"]}");
        await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.Equal(["c"], model.Tags);
    }

    [Fact]
    public async Task An_empty_multi_selection_clears_the_model()
    {
        var model = new TagsModel { Tags = ["a"] };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Tags).Multiple(true)[Option.Value("a"), Option.Value("b")]
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = Markup.Attr(html, "data-rask-on-change");
        using var doc = JsonDocument.Parse("{\"value\":\"\",\"values\":[]}");
        await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.Empty(model.Tags);
    }

    [Fact]
    public void A_bound_multi_select_preselects_every_bound_option()
    {
        // The render half. A single-value select marks the one option matching its formatted value;
        // a multi-select has to mark each member of the bound collection.
        var model = new TagsModel { Tags = ["a", "c"] };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Tags).Multiple(true)[Option.Value("a"), Option.Value("b"), Option.Value("c")]
        ]);

        var html = view.RenderAsLiveRoot();

        Assert.Contains("<option value=\"a\" selected>", html);
        Assert.Contains("<option value=\"c\" selected>", html);
        Assert.DoesNotContain("<option value=\"b\" selected>", html);
    }

    [Fact]
    public async Task A_multi_select_frame_without_the_values_array_falls_back_to_the_single_value()
    {
        // A browser holding a client cached from a deploy that predates the array still sends `value`
        // alone. Reporting one option is wrong, but dropping the user's pick entirely is worse.
        var model = new TagsModel { Tags = [] };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Tags).Multiple(true)[Option.Value("a"), Option.Value("b")]
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = Markup.Attr(html, "data-rask-on-change");
        using var doc = JsonDocument.Parse("{\"value\":\"b\"}");
        await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.Equal(["b"], model.Tags);
    }

    [Fact]
    public async Task A_get_only_collection_is_refilled_in_place()
    {
        // `public List<string> Tags { get; } = [];` is the ordinary way to declare one of these, and it
        // has no setter — the shape the existing MultiSelect sample uses. Assigning would throw; the
        // model's own collection is refilled instead.
        var model = new OwnedTagsModel();
        model.Tags.Add("a");
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Tags).Multiple(true)[Option.Value("a"), Option.Value("b"), Option.Value("c")]
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = Markup.Attr(html, "data-rask-on-change");
        using var doc = JsonDocument.Parse("{\"value\":\"b\",\"values\":[\"b\",\"c\"]}");
        var ok = await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.True(ok);
        Assert.Equal(["b", "c"], model.Tags);
    }

    [Fact]
    public async Task A_multi_select_over_a_scalar_keeps_the_single_value_handler()
    {
        // Multiple:true on a model that can only hold one answer. Silently widening it would be the
        // more surprising change, so this stays on the single-value path.
        var model = new ColorPicker { Color = null };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Color).Multiple(true)[Option.Value("red"), Option.Value("blue")]
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = Markup.Attr(html, "data-rask-on-change");
        using var doc = JsonDocument.Parse("{\"value\":\"red\",\"values\":[\"red\",\"blue\"]}");
        await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.Equal("red", model.Color);
    }

    [Fact]
    public async Task A_stated_type_argument_over_a_list_property_assigns_a_list()
    {
        // The type argument is STATED here, not inferred, so it is `ICollection<string>` while the
        // property is `List<string>`. That divergence used to reach the "satisfied by an array" branch
        // and assign a string[] to a List<string> property, which threw ArgumentException from inside
        // the change handler -- so it arrived as a failed frame rather than at the call site.
        var model = new ListTagsModel();
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind<ICollection<string>>(() => model.Tags).Multiple(true)[
                Option.Value("a"), Option.Value("b"), Option.Value("c")]
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = Markup.Attr(html, "data-rask-on-change");
        using var doc = JsonDocument.Parse("{\"value\":\"a\",\"values\":[\"a\",\"c\"]}");
        var ok = await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.True(ok);
        Assert.Equal(["a", "c"], model.Tags);
    }

    [Fact]
    public async Task A_stated_type_argument_over_a_set_property_assigns_a_set()
    {
        var model = new SetTagsModel();
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind<ICollection<string>>(() => model.Tags).Multiple(true)[
                Option.Value("a"), Option.Value("b"), Option.Value("c")]
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = Markup.Attr(html, "data-rask-on-change");
        using var doc = JsonDocument.Parse("{\"value\":\"b\",\"values\":[\"b\",\"c\"]}");
        var ok = await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.True(ok);
        Assert.Equal(["b", "c"], model.Tags.OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_stated_type_argument_over_an_array_property_assigns_an_array()
    {
        // The declared type decides, in both directions: an array property still gets an array.
        var model = new TagsModel { Tags = [] };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind<IEnumerable<string>>(() => model.Tags).Multiple(true)[
                Option.Value("a"), Option.Value("b")]
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = Markup.Attr(html, "data-rask-on-change");
        using var doc = JsonDocument.Parse("{\"value\":\"a\",\"values\":[\"a\",\"b\"]}");
        var ok = await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.True(ok);
        Assert.Equal(["a", "b"], model.Tags);
    }

    [Fact]
    public async Task An_interface_property_gets_a_collection_that_can_still_grow()
    {
        // An array satisfies ICollection<string>, so assigning one never threw here -- it just left the
        // model holding a FIXED-SIZE collection, and the next Add threw somewhere else entirely. A list
        // satisfies the same property and stays growable.
        var model = new InterfaceTagsModel();
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Tags).Multiple(true)[Option.Value("a"), Option.Value("b")]
        ]);
        var html = view.RenderAsLiveRoot();

        var changeId = Markup.Attr(html, "data-rask-on-change");
        using var doc = JsonDocument.Parse("{\"value\":\"a\",\"values\":[\"a\"]}");
        var ok = await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

        Assert.True(ok);
        Assert.Equal(["a"], model.Tags);

        model.Tags.Add("b");
        Assert.Equal(["a", "b"], model.Tags);
    }

    private sealed class ColorPicker
    {
        public string? Color { get; set; }
    }

    private sealed class TagsModel
    {
        public string[] Tags { get; set; } = [];
    }

    private sealed class OwnedTagsModel
    {
        public List<string> Tags { get; } = [];
    }

    private sealed class ListTagsModel
    {
        public List<string> Tags { get; set; } = [];
    }

    private sealed class SetTagsModel
    {
        public HashSet<string> Tags { get; set; } = [];
    }

    private sealed class InterfaceTagsModel
    {
        public ICollection<string> Tags { get; set; } = new List<string>();
    }

    private sealed class ChoiceModel
    {
        public int? Choice { get; set; }
    }

    private sealed class StatusModel
    {
        public SelectStatus? Status { get; set; }
    }

    private enum SelectStatus { Active, Inactive }
}
