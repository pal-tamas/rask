using System.Text.Json;
using Rask.Core.Tests.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public class SelectTests
{
    // Preselection (MarkSelected) only runs over children passed through the factory's
    // `params IEnumerable<Child> Children` slot — children attached via the indexer
    // overwrite the preselected list. These tests therefore use the `Children:` named
    // arg form rather than `Select(...)[Option(...)]`.

    [Fact]
    public void BoundSelect_NullValue_Preselects_EmptyValueOption()
    {
        var model = new ColorPicker { Color = null };
        var view = new StubComponent(() => Form(model)[
            Select(() => model.Color, Children: new Child[] { Option(""), Option("red") })
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
        var view = new StubComponent(() => Form(model)[
            Select(() => model.Color, Children: new Child[] { Option(""), Option("red"), Option("blue") })
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
        var view = new StubComponent(() => Form(model)[
            Select(() => model.Color, Children: new Child[] { Option(""), Option("red") })
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
        var view = new StubComponent(() => Form(model)[
            Select(() => model.Choice, Children: new Child[] { Option(""), Option("5"), Option("10") })
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
        var view = new StubComponent(() => Form(model)[
            Select(() => model.Choice, Children: new Child[] { Option(""), Option("5") })
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
        var view = new StubComponent(() => Form(model)[
            Select(() => model.Status, Children: new Child[] { Option(""), Option("Active"), Option("Inactive") })
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
        var view = new StubComponent(() => Form(model)[
            Select(() => model.Status, Children: new Child[] { Option(""), Option("Active") })
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
        var view = new StubComponent(() => Form(model)[
            Select(() => model.Color, Children: new Child[] { Option()["placeholder"], Option("red") })
        ]);

        var html = view.RenderAsLiveRoot();

        Assert.DoesNotContain("selected", html);
    }

    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<select></select>", Select().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<select id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" name=\"n\" multiple required disabled size=\"5\" form=\"f\" autofocus autocomplete=\"off\"></select>",
            Select("n", true, true, true, 5, "f", true, "off", Id: "i", Class: "c", Style: "s",
                Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<select>&lt;x&gt;</select>", Select()["<x>"].ToHtml());

    [Fact]
    public void Render_OnChangeOutsideLiveContext_OmitsHandlerAttribute() =>
        Assert.Equal(
            "<select></select>",
            Select(OnChange: _ => { }).ToHtml());

    [Fact]
    public void Render_OnChangeInsideLiveContext_EmitsDataRaskOnChange()
    {
        var view = new StubComponent(() => Select(OnChange: _ => { }));
        Assert.Equal(
            "<select data-rask-on-change=\"h0\"></select>",
            view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_OnChangeAsyncInsideLiveContext_EmitsDataRaskOnChange()
    {
        var view = new StubComponent(() => Select(OnChangeAsync: async _ => { await Task.Yield(); }));
        Assert.Equal(
            "<select data-rask-on-change=\"h0\"></select>",
            view.RenderAsLiveRoot());
    }

    private sealed class ColorPicker
    {
        public string? Color { get; set; }
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
