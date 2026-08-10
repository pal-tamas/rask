namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsSelect (controlled mode). The custom box is a `.form-select` display
// combobox `<div>` (showing the selected option's rich label, or a placeholder) that opens a `.dropdown-menu`
// listbox of role=option buttons; supplying a Filter predicate adds a search field in the dropdown (only
// present while open, so not in this static closed markup). Native: true drops to the plain `<select>`.
public partial class BsSelectTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Select_Empty_RendersComboboxBoxWithPlaceholderAndOptionButtons()
    {
        var html = BsSelect<string>(Options: ["a", "b"], Value: null, Id: "s").ToHtml();
        Assert.Contains(
            "<div id=\"s\" class=\"form-select\" data-rask-anchor=\"\" role=\"combobox\" tabindex=\"0\" " +
            "aria-haspopup=\"listbox\" aria-expanded=\"false\" aria-controls=\"s-list\">" +
            "<span class=\"text-secondary\">Select&#x2026;</span></div>", html);
        Assert.Contains(
            "<div id=\"s-list\" class=\"dropdown-menu\" role=\"listbox\">" +
            "<button id=\"s-opt-0\" class=\"dropdown-item\" data-rask-key=\"0\" role=\"option\" type=\"button\">a</button>",
            html);
    }

    [Fact]
    public void Select_WithSelection_ShowsLabelInBoxAndMarksActiveOption()
    {
        var html = BsSelect<string>(Options: ["a", "b", "c"], Value: "b", Id: "s").ToHtml();
        Assert.Contains("aria-controls=\"s-list\">b</div>", html);
        Assert.Contains(
            "<button id=\"s-opt-1\" class=\"dropdown-item active\" data-rask-key=\"1\" role=\"option\" " +
            "aria-selected=\"true\" type=\"button\">b</button>", html);
    }

    [Fact]
    public void Select_CustomPlaceholderAndLabel_LabelSitsAboveTheBox()
    {
        var html = BsSelect<string>(Options: [], Value: null, Label: "Plan", Placeholder: "Pick one", Id: "p").ToHtml();
        // The combobox is a <div role="combobox"> (not labelable), so the label is associated by id via the
        // box's aria-labelledby rather than a void <label for> pointing at the div.
        Assert.Contains("<label id=\"p-label\" class=\"form-label\">Plan</label>", html);
        Assert.Contains("aria-labelledby=\"p-label\"", html);
        Assert.Contains("<span class=\"text-secondary\">Pick one</span>", html);
    }

    [Fact]
    public void Select_Required_AppendsAsteriskToLabel() =>
        Assert.Contains(
            "<label id=\"s-label\" class=\"form-label\">Plan<span class=\"text-danger ms-1\">*</span></label>",
            BsSelect<string>(Options: [], Value: null, Label: "Plan", Required: true, Id: "s").ToHtml());

    [Fact]
    public void Select_Floating_WrapsBoxAndLabelInFormFloatingWithBlankBox()
    {
        // Float-only-when-filled: the empty box carries no placeholder text (the label is the placeholder),
        // the wrapper is .form-floating.bs-floating, and .bs-floating-filled is absent.
        var html = BsSelect<string>(Options: [], Value: null, Label: "Plan", Floating: true, Id: "s").ToHtml();
        Assert.Contains("<div class=\"form-floating bs-floating position-relative\">", html);
        Assert.Contains(
            "aria-controls=\"s-list\" aria-labelledby=\"s-label\"></div><label id=\"s-label\">Plan</label>", html);
    }

    [Fact]
    public void Select_FloatingWithValue_AddsFilledMarker() =>
        Assert.Contains(
            "<div class=\"form-floating bs-floating bs-floating-filled position-relative\">",
            BsSelect<string>(Options: ["a"], Value: "a", Label: "Plan", Floating: true, Id: "s").ToHtml());

    [Fact]
    public void Select_Disabled_DropsInteractivityAndDisablesOptions()
    {
        var html = BsSelect<string>(Options: ["a"], Value: null, Disabled: true, Id: "s").ToHtml();
        Assert.Contains("class=\"form-select disabled pe-none\"", html);
        Assert.DoesNotContain("tabindex=\"0\"", html);
        Assert.Contains("role=\"option\" type=\"button\" disabled>a</button>", html);
    }

    [Fact]
    public void Select_NullableWithValue_ShowsClearButtonAndPadsBox()
    {
        // A nullable (Nullable<T>) select with a value pads the box and adds the × clear (btn-close).
        var html = BsSelect<int?>(Options: new int?[] { 1, 2 }, Value: 2, Id: "s").ToHtml();
        Assert.Contains("<div id=\"s\" class=\"form-select bs-select-clearable\"", html);
        Assert.Contains(
            "<button class=\"btn-close position-absolute top-50 translate-middle-y bs-select-clear\" " +
            "aria-label=\"Clear\" type=\"button\"></button>", html);
        // A *closed* select's × never carries the raised z-index (.bs-clear-open) — that hook is added only
        // while open, so a stray × can't paint over another (open) select's dropdown menu.
        Assert.DoesNotContain("bs-clear-open", html);
    }

    [Fact]
    public void Select_NonNullable_HasNoClearButton() =>
        Assert.DoesNotContain("bs-select-clear",
            BsSelect<string>(Options: ["a"], Value: "a", Id: "s").ToHtml());

    private sealed record Team(int Id, string Name);

    [Fact]
    public void Select_ValueSelector_BindsProjectedValueWhileRenderingObjects()
    {
        // Options are objects; the bound value is a projected field (OptionValue). The box shows the
        // selected object's label, and the option whose OptionValue equals the bound value is marked active.
        var teams = new[] { new Team(1, "Platform"), new Team(2, "Growth") };
        var html = BsSelect<int?, Team>(Options: teams, OptionValue: t => t.Id,
            OptionLabel: t => Text.Value(t.Name), Value: 2, Id: "s").ToHtml();
        Assert.Contains("aria-controls=\"s-list\">Growth</div>", html);
        Assert.Contains(
            "<button id=\"s-opt-1\" class=\"dropdown-item active\" data-rask-key=\"1\" role=\"option\" " +
            "aria-selected=\"true\" type=\"button\">Growth</button>", html);
    }

    [Fact]
    public void Select_ValueSelector_Native_UsesProjectedValueAsOptionValue() =>
        // The native <select> option values are the projected values, so binding round-trips the id.
        Assert.Contains("value=\"2\">Growth</option>",
            BsSelect<int?, Team>(Options: new[] { new Team(1, "Platform"), new Team(2, "Growth") },
                OptionValue: t => t.Id, OptionLabel: t => Text.Value(t.Name), Value: 1, Native: true, Id: "s").ToHtml());

    [Fact]
    public void Select_Native_RendersPlainSelectWithSelectedOptionAndPlaceholder() =>
        // The selected option KEEPS its reconciliation key (data-rask-key="0"): dropping it when marking the
        // option selected shifts every option's key across renders, breaks keyed diffing, and leaves the
        // browser's live `selected` property desynced so the <select> snaps back to the old value.
        Assert.Equal(
            "<div><select id=\"t\" class=\"form-select\">" +
            "<option data-rask-key=\"placeholder\" value=\"\" disabled>Pick</option>" +
            "<option data-rask-key=\"0\" value=\"a\" selected>a</option>" +
            "<option data-rask-key=\"1\" value=\"b\">b</option>" +
            "</select></div>",
            BsSelect<string>(Options: ["a", "b"], Value: "a", Native: true, Placeholder: "Pick", Id: "t").ToHtml());

    [Fact]
    public void Select_Nullable_NonFloating_WrapsBoxAndClearInPositionRelative()
    {
        // The × is absolutely placed; wrapping just the box + × in a position-relative div anchors it to the
        // box alone. Without the wrapper it would centre over the label-above + box and land on the box's top
        // edge. (A non-clearable select needs no wrapper — see Select_Empty_… which has the box bare.)
        var html = BsSelect<int?>(Options: new int?[] { 1, 2 }, Value: 2, Label: "Seats", Id: "s").ToHtml();
        Assert.Contains(
            "<div class=\"position-relative\"><div id=\"s\" class=\"form-select bs-select-clearable\"", html);
        Assert.Contains("bs-select-clear", html);
    }

    [Fact]
    public void Select_OptionDisabled_Custom_RendersAriaDisabledOptionWithNoHandler() =>
        // The "b" option is disabled: still a role="option", but greyed via aria-disabled and non-clickable.
        Assert.Contains(
            "<button id=\"s-opt-1\" class=\"dropdown-item\" data-rask-key=\"1\" role=\"option\" " +
            "aria-disabled=\"true\" type=\"button\" disabled>b</button>",
            BsSelect<string>(Options: ["a", "b"], Value: "a", OptionDisabled: o => o == "b", Id: "s").ToHtml());

    [Fact]
    public void Select_OptionDisabled_Native_RendersDisabledOption() =>
        Assert.Contains("<option data-rask-key=\"1\" value=\"b\" disabled>b</option>",
            BsSelect<string>(Options: ["a", "b"], Value: "a", Native: true, OptionDisabled: o => o == "b", Id: "t")
                .ToHtml());

    [Fact]
    public void Select_OptionGroup_Custom_RendersDropdownHeadersInFirstSeenOrder()
    {
        var html = BsSelect<string>(Options: ["a", "b"], Value: "a",
            OptionGroup: o => o == "a" ? "First" : "Second", Id: "s").ToHtml();
        Assert.Contains("<div class=\"dropdown-header\" data-rask-key=\"hdr-First\">First</div>", html);
        Assert.Contains("<div class=\"dropdown-header\" data-rask-key=\"hdr-Second\">Second</div>", html);
    }

    [Fact]
    public void Select_OptionGroup_Native_WrapsOptionsInOptgroups()
    {
        var html = BsSelect<string>(Options: ["a", "b"], Value: "a", Native: true,
            OptionGroup: o => o == "a" ? "First" : "Second", Id: "s").ToHtml();
        // Each group is an <optgroup label> (own ordinal key); options keep their global flat key + value.
        Assert.Contains("<optgroup data-rask-key=\"grp-0\" label=\"First\">" +
            "<option data-rask-key=\"0\" value=\"a\" selected>a</option></optgroup>", html);
        Assert.Contains("<optgroup data-rask-key=\"grp-1\" label=\"Second\">" +
            "<option data-rask-key=\"1\" value=\"b\">b</option></optgroup>", html);
    }
}
