#pragma warning disable RASK014 // StubComponent constructed directly in tests

using System.Text.Json;

namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsMultiSelect (controlled mode). The `.form-select` box shows the selected
// chips (BsBadge + BsCloseButton) or a placeholder; each option row is a dropdown-item button with a
// checkbox reflecting selection. Supplying a Filter predicate adds a search field in the dropdown (only
// present while open, so absent from this static closed markup). ToHtml() renders with the menu closed.
public partial class BsMultiSelectTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void MultiSelect_Empty_ShowsPlaceholderBoxAndUncheckedOptions()
    {
        var html = BsMultiSelect.Value(new List<string>()).Options(["a", "b"]).Id("m").ToHtml();
        // The box is a role="combobox" naming its listbox via aria-controls (id pinned so the derived
        // list id is deterministic).
        Assert.Contains(
            "<div class=\"form-select h-auto d-flex flex-wrap align-items-center gap-1\" data-rask-anchor=\"\" " +
            "role=\"combobox\" tabindex=\"0\" aria-haspopup=\"listbox\" aria-expanded=\"false\" " +
            "aria-controls=\"m-list\"><span class=\"text-secondary\">Select&#x2026;</span></div>", html);
        Assert.Contains(
            "<div id=\"m-list\" class=\"dropdown-menu\" role=\"listbox\" aria-multiselectable=\"true\">", html);
        // Each row is a proper listbox option: id (for aria-activedescendant), role="option", aria-selected.
        Assert.Contains(
            "<button id=\"m-opt-0\" class=\"dropdown-item d-flex align-items-center gap-2\" data-rask-key=\"0\" " +
            "role=\"option\" aria-selected=\"false\" type=\"button\">" +
            "<input class=\"form-check-input m-0 pe-none\" type=\"checkbox\" />a</button>", html);
    }

    [Fact]
    public void MultiSelect_WithSelection_RendersChipsAndChecksSelectedOptions()
    {
        var html = BsMultiSelect.Value(new List<string> { "a", "c" }).Options(["a", "b", "c"]).ToHtml();
        Assert.Contains(
            "<span class=\"badge text-bg-primary d-inline-flex align-items-center\" data-rask-key=\"0\">a" +
            "<button class=\"btn-close btn-close-white ms-1\" aria-label=\"Close\" type=\"button\"></button></span>", html);
        Assert.Contains("type=\"checkbox\" checked />a", html);
        Assert.Contains("type=\"checkbox\" checked />c", html);
    }

    [Fact]
    public void MultiSelect_Label_SitsAboveTheControl()
    {
        // The label is associated with the combobox via aria-labelledby (the box is a <div role="combobox">,
        // not a labelable element for <label for>).
        var html = BsMultiSelect.Value(new List<string>()).Options([]).Label("Tags").Id("m").ToHtml();
        Assert.Contains("<label id=\"m-label\" class=\"form-label\">Tags</label>", html);
        Assert.Contains("aria-labelledby=\"m-label\"", html);
    }

    [Fact]
    public void MultiSelect_CustomPlaceholder_ReplacesDefault() =>
        Assert.Contains("<span class=\"text-secondary\">Pick tags</span>",
            BsMultiSelect.Value(new List<string>()).Options([]).Placeholder("Pick tags").ToHtml());

    [Fact]
    public void MultiSelect_FloatingEmpty_WrapsInFormFloatingWithBlankBox()
    {
        // Float-only-when-filled: the empty box carries NO "Select…" placeholder span (the centred floating
        // label is the placeholder) — otherwise the two texts overlap. Wrapper is .form-floating.bs-floating
        // with no .bs-floating-filled. Guards the regression where the leftover placeholder overlapped the label.
        var html = BsMultiSelect
            .Value(new List<string>())
            .Options([])
            .Label("Interests")
            .Floating(true)
            .Id("m")
            .ToHtml();
        Assert.Contains("<div class=\"form-floating bs-floating position-relative\">", html);
        Assert.Contains(
            "aria-controls=\"m-list\" aria-labelledby=\"m-label\"></div><label id=\"m-label\">Interests</label>",
            html);
        Assert.DoesNotContain("Select&#x2026;", html);
        Assert.DoesNotContain("bs-floating-filled", html);
    }

    [Fact]
    public void MultiSelect_FloatingWithChips_AddsFilledMarker()
    {
        var html = BsMultiSelect
            .Value(new List<string> { "a" })
            .Options(["a"])
            .Label("Interests")
            .Floating(true)
            .ToHtml();
        Assert.Contains("<div class=\"form-floating bs-floating bs-floating-filled position-relative\">", html);
        Assert.DoesNotContain("Select&#x2026;", html);
    }

    // `MultiSelect_RequiresExactlyOneOfBindOrValue` was here. Its name describes what the chain now
    // guarantees outright: `Bind` and `Value` are the two openings, taking either yields a state the
    // other is not reachable from, and taking neither yields no component. "Exactly one" is the shape of
    // the type, so the runtime guard it used to exercise is unreachable and the test is gone with it.

    [Fact]
    public async Task MultiSelect_Bound_Invalid_WiresAriaInvalidDescribedbyAndAlertFeedback()
    {
        // A bound multiselect that fails validation must expose the failure to assistive tech: is-invalid +
        // aria-invalid + aria-describedby on the combobox box, and a role="alert" error region with the id.
        var model = new TagModel();
        var view = new StubComponent(() => Form.Model(model)[
            BsMultiSelect.Bind(() => model.Tags)
                .Options(["a", "b"])
                .Id("m")
                .Validate(v => v.Count == 0 ? new[] { "pick a tag" } : Array.Empty<string>())
        ]);

        var html = view.RenderAsLiveRoot();
        var submitId = Markup.Attr(html, "data-rask-on-submit")!;
        using var payload = JsonDocument.Parse("{\"form\":{}}");
        await view.TryInvokeHandlerAsync(submitId, payload.RootElement);

        var after = view.RenderAsLiveRoot();
        Assert.Contains("is-invalid", after);
        Assert.Contains("aria-invalid=\"true\" aria-describedby=\"m-error\"", after);
        Assert.Contains(
            "<div id=\"m-error\" class=\"invalid-feedback d-block\" role=\"alert\">pick a tag</div>", after);
    }

    [Fact]
    public void MultiSelect_OptionDisabled_RendersAriaDisabledOption() =>
        // A per-option-disabled row keeps role="option" + aria-selected but adds aria-disabled and the disabled
        // attribute; the enabled rows are unaffected.
        Assert.Contains(
            "<button id=\"m-opt-1\" class=\"dropdown-item d-flex align-items-center gap-2\" data-rask-key=\"1\" " +
            "role=\"option\" aria-selected=\"false\" aria-disabled=\"true\" type=\"button\" disabled>",
            BsMultiSelect
                .Value(new List<string>())
                .Options(["a", "b"])
                .OptionDisabled(o => o == "b")
                .Id("m")
                .ToHtml());

    [Fact]
    public void MultiSelect_SelectAll_RendersSelectAllHeader() =>
        // Opt-in header row at the top of the menu; "Select all" while not everything is selected.
        Assert.Contains("fw-semibold\" type=\"button\">Select all</button>",
            BsMultiSelect.Value(new List<string>()).Options(["a", "b"]).SelectAll(true).Id("m").ToHtml());

    [Fact]
    public void MultiSelect_SelectAll_ShowsClearAll_WhenAllEnabledSelected() =>
        Assert.Contains(">Clear all</button>",
            BsMultiSelect
                .Value(new List<string> { "a", "b" })
                .Options(["a", "b"])
                .SelectAll(true)
                .ToHtml());

    [Fact]
    public void MultiSelect_OptionGroup_RendersDropdownHeadersInFirstSeenOrder()
    {
        var html = BsMultiSelect
            .Value(new List<string>())
            .Options(["a", "b"])
            .OptionGroup(o => o == "a" ? "G1" : "G2")
            .Id("m")
            .ToHtml();
        Assert.Contains("<div class=\"dropdown-header\" data-rask-key=\"hdr-G1\">G1</div>", html);
        Assert.Contains("<div class=\"dropdown-header\" data-rask-key=\"hdr-G2\">G2</div>", html);
    }

    private sealed class TagModel
    {
        public List<string> Tags { get; set; } = [];
    }
}
