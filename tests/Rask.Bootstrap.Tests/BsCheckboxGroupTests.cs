#pragma warning disable RASK014 // StubComponent constructed directly in tests

using System.Text.Json;

namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsCheckboxGroup. Each option is a <div class="form-check"> holding a
// labelable checkbox whose <label for> associates by id; selection reflects ICollection membership. A
// group Label wraps the set in a <fieldset> named by a <legend>; without it the bare per-item fragment
// is kept. Validation surfaces as a role="alert" live region the boxes point at via aria-describedby.
public partial class BsCheckboxGroupTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void CheckboxGroup_Controlled_ReflectsMembershipWithPerItemLabels()
    {
        var html = BsCheckboxGroup(Options: ["a", "b"], Value: new List<string> { "a" }, Name: "tags").ToHtml();
        Assert.DoesNotContain("<fieldset", html);
        Assert.StartsWith("<div class=\"form-check\" data-rask-key=\"0\">", html);
        Assert.Contains(
            "<input id=\"tags-0\" class=\"form-check-input\" type=\"checkbox\" name=\"tags\" value=\"a\" checked />",
            html);
        Assert.Contains("<label class=\"form-check-label\" for=\"tags-0\">a</label>", html);
        Assert.Contains(
            "<input id=\"tags-1\" class=\"form-check-input\" type=\"checkbox\" name=\"tags\" value=\"b\" />", html);
    }

    [Fact]
    public void CheckboxGroup_WithLabel_WrapsInFieldsetNamedByLegend()
    {
        var html = BsCheckboxGroup(Options: ["a", "b"], Value: new List<string>(), Name: "tags",
            Label: "Tags").ToHtml();
        Assert.StartsWith("<fieldset class=\"border-0 p-0 m-0\">", html);
        Assert.Contains("<legend class=\"form-label fs-6\">Tags</legend>", html);
        Assert.EndsWith("</fieldset>", html);
    }

    [Fact]
    public void CheckboxGroup_Disabled_DisablesEveryBox()
    {
        var html = BsCheckboxGroup(Options: ["a", "b"], Value: new List<string>(), Name: "tags",
            Disabled: true).ToHtml();
        Assert.Contains("value=\"a\" disabled />", html);
        Assert.Contains("value=\"b\" disabled />", html);
    }

    [Fact]
    public async Task CheckboxGroup_Bound_Invalid_WiresAriaInvalidDescribedbyAndAlertFeedback()
    {
        var model = new TagModel();
        var view = new StubComponent(() => Form(model)[
            BsCheckboxGroup(() => model.Tags, ["a", "b"], Label: "Tags",
                Validate: v => v.Count == 0 ? new[] { "pick at least one" } : Array.Empty<string>())
        ]);

        var html = view.RenderAsLiveRoot();
        var submitId = Markup.Attr(html, "data-rask-on-submit")!;
        using var payload = JsonDocument.Parse("{\"form\":{}}");
        await view.TryInvokeHandlerAsync(submitId, payload.RootElement);

        var after = view.RenderAsLiveRoot();
        Assert.Contains("aria-invalid=\"true\" aria-describedby=\"Tags-error\"", after);
        Assert.Contains(
            "<div id=\"Tags-error\" class=\"invalid-feedback d-block\" role=\"alert\">pick at least one</div>",
            after);
    }

    [Fact]
    public async Task CheckboxGroup_Bound_TogglesCollectionMembership()
    {
        // End-to-end through the control's ToggleAsync: unchecking option "a" removes it from the bound
        // collection; re-checking it adds it back (checkbox change handlers report the box's "true"/"false").
        var model = new TagModel { Tags = { "a" } };
        var view = new StubComponent(() => Form(model)[
            BsCheckboxGroup(() => model.Tags, ["a", "b"], Name: "tags")
        ]);

        var html = view.RenderAsLiveRoot();
        var aChangeId = Markup.Attr(html, "data-rask-on-change")!; // first checkbox = option "a"

        using (var off = JsonDocument.Parse("{\"value\":\"false\"}"))
        {
            await view.TryInvokeHandlerAsync(aChangeId, off.RootElement);
        }

        Assert.Empty(model.Tags);

        using (var on = JsonDocument.Parse("{\"value\":\"true\"}"))
        {
            await view.TryInvokeHandlerAsync(aChangeId, on.RootElement);
        }

        Assert.Equal(new[] { "a" }, model.Tags);
    }

    [Fact]
    public void CheckboxGroup_NeitherBindNorValue_Throws() =>
        Assert.Throws<InvalidOperationException>(() => BsCheckboxGroup(Options: ["a"]).ToHtml());

    private sealed class TagModel
    {
        public List<string> Tags { get; set; } = [];
    }
}
