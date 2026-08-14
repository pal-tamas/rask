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
    public void BoundSelect_IndexerChildren_PreselectsMatchingNonFirstOption()
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
    public void BoundSelect_IndexerChildren_NullValue_PreselectsEmptyOption()
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
    public void BoundSelect_NullValue_Preselects_EmptyValueOption()
    {
        var model = new ColorPicker { Color = null };
        var view = new StubComponent(() => Form.Model(model)[
            Select.Bind(() => model.Color)[Option.Value(""), Option.Value("red")]
        ]);

        var html = view.RenderAsLiveRoot();

        // Empty-value option gets `selected` because FormatValue(null) == "" matches opt.Value.
        Assert.Contains("<option value=\"\" selected>", html);
        Assert.DoesNotContain("<option value=\"red\" selected>", html);
    }

    [Fact]
    public void BoundSelect_NonNullValue_PreselectsMatchingOption()
    {
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
    public async Task BoundSelect_NullableString_EmptyChange_SetsPropertyToNull()
    {
        // `string?` is nullable per the C# NRT annotation; BindingHelpers reads it via
        // NullabilityInfoContext and treats empty input as null — matching Nullable<T>
        // value-type behavior. A non-nullable `string` property would set "" instead
        // (see OnInput_NonNullableString_EmptyInput_SetsEmptyString in FormBindingTests).
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
    public async Task BoundSelect_NullableInt_ValidChange_SetsTypedValue()
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
    public async Task BoundSelect_NullableInt_EmptyChange_SetsPropertyToNull()
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
    public async Task BoundSelect_NullableEnum_ValidChange_ParsesEnum()
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
    public async Task BoundSelect_NullableEnum_EmptyChange_SetsPropertyToNull()
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
    public void BoundSelect_OptionWithoutValueAttribute_IsNotPreselected_ForNullBoundValue()
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
    public void BoundSelect_MarkedOption_KeepsItsReconciliationKey()
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
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<select></select>", Select<string>().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<select id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" name=\"n\" multiple required disabled size=\"5\" form=\"f\" autofocus autocomplete=\"off\"></select>",
            Select<string>("n", true, true, true, 5, "f", true, "off", Id: "i", Class: "c", Style: "s",
                Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<select>&lt;x&gt;</select>", Select<string>()["<x>"].ToHtml());

    [Fact]
    public void Render_OnChangeOutsideLiveContext_OmitsHandlerAttribute() =>
        Assert.Equal(
            "<select></select>",
            Select<string>(OnChange: _ => { }).ToHtml());

    [Fact]
    public void Render_OnChangeInsideLiveContext_EmitsDataRaskOnChange()
    {
        var view = new StubComponent(() => Select<string>(OnChange: _ => { }));
        Assert.Equal(
            "<select data-rask-on-change=\"h0\"></select>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnChangeAsyncInsideLiveContext_EmitsDataRaskOnChange()
    {
        var view = new StubComponent(() => Select<string>(OnChangeAsync: async _ => { await Task.Yield(); }));
        Assert.Equal(
            "<select data-rask-on-change=\"h0\"></select>",
            view.RenderAsLiveRoot());
    }

    // #595 — a <select multiple> reports its whole selection through the frame's `values` array.
    // `select.value` is only the FIRST selected option (the DOM has no multi-value `value`), so the model
    // used to converge on one option out of however many the user picked, from a report that was the
    // wrong shape rather than merely late.

    [Fact]
    public async Task BoundMultiSelect_Change_BindsEveryReportedOption()
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
    public async Task BoundMultiSelect_Change_ReplacesRatherThanMerges()
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
    public async Task BoundMultiSelect_EmptySelection_ClearsTheModel()
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
    public void BoundMultiSelect_PreselectsEveryBoundOption()
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
    public async Task BoundMultiSelect_WithoutTheValuesArray_FallsBackToTheSingleValue()
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
    public async Task BoundMultiSelect_GetOnlyCollection_IsRefilledInPlace()
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
    public async Task BoundMultiSelect_OverAScalar_KeepsTheSingleValueHandler()
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
