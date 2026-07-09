namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsSelect (controlled mode). By default it renders the custom dropdown — a
// .form-select combobox box + a .dropdown-menu listbox of role=option buttons — mirroring BsMultiSelect;
// Native: true drops to the plain native <select>. ToHtml() renders static markup with the menu closed
// (open/cursor are live-runtime view state), so an explicit Id keeps the option ids deterministic.
public class BsSelectTests
{
    [Fact]
    public void Select_Empty_ShowsPlaceholderBoxAndOptionButtons() =>
        Assert.Equal(
            "<div class=\"dropdown\" data-rask-popover=\"\">" +
            "<div id=\"s\" class=\"form-select\" data-rask-anchor=\"\" role=\"combobox\" tabindex=\"0\" " +
            "aria-haspopup=\"listbox\" aria-expanded=\"false\" aria-controls=\"s-list\">" +
            "<span class=\"text-secondary\">Select&#x2026;</span></div>" +
            "<div id=\"s-list\" class=\"dropdown-menu\" role=\"listbox\">" +
            "<button id=\"s-opt-0\" class=\"dropdown-item\" data-rask-key=\"0\" role=\"option\" type=\"button\">a</button>" +
            "<button id=\"s-opt-1\" class=\"dropdown-item\" data-rask-key=\"1\" role=\"option\" type=\"button\">b</button>" +
            "</div></div>",
            BsSelect<string>(Options: ["a", "b"], Value: null, Id: "s").ToHtml());

    [Fact]
    public void Select_WithSelection_ShowsLabelInBoxAndMarksActiveOption() =>
        Assert.Equal(
            "<div class=\"dropdown\" data-rask-popover=\"\">" +
            "<div id=\"s\" class=\"form-select\" data-rask-anchor=\"\" role=\"combobox\" tabindex=\"0\" " +
            "aria-haspopup=\"listbox\" aria-expanded=\"false\" aria-controls=\"s-list\">b</div>" +
            "<div id=\"s-list\" class=\"dropdown-menu\" role=\"listbox\">" +
            "<button id=\"s-opt-0\" class=\"dropdown-item\" data-rask-key=\"0\" role=\"option\" type=\"button\">a</button>" +
            "<button id=\"s-opt-1\" class=\"dropdown-item active\" data-rask-key=\"1\" role=\"option\" " +
            "aria-selected=\"true\" type=\"button\">b</button>" +
            "<button id=\"s-opt-2\" class=\"dropdown-item\" data-rask-key=\"2\" role=\"option\" type=\"button\">c</button>" +
            "</div></div>",
            BsSelect<string>(Options: ["a", "b", "c"], Value: "b", Id: "s").ToHtml());

    [Fact]
    public void Select_CustomPlaceholderAndLabel_LabelSitsAboveTheBox() =>
        Assert.Equal(
            "<div class=\"dropdown\" data-rask-popover=\"\">" +
            "<label class=\"form-label\" for=\"p\">Plan</label>" +
            "<div id=\"p\" class=\"form-select\" data-rask-anchor=\"\" role=\"combobox\" tabindex=\"0\" " +
            "aria-haspopup=\"listbox\" aria-expanded=\"false\" aria-controls=\"p-list\">" +
            "<span class=\"text-secondary\">Pick one</span></div>" +
            "<div id=\"p-list\" class=\"dropdown-menu\" role=\"listbox\"></div></div>",
            BsSelect<string>(Options: [], Value: null, Label: "Plan", Placeholder: "Pick one", Id: "p").ToHtml());

    [Fact]
    public void Select_Required_AppendsAsteriskToLabel() =>
        Assert.Contains(
            "<label class=\"form-label\" for=\"s\">Plan<span class=\"text-danger ms-1\">*</span></label>",
            BsSelect<string>(Options: [], Value: null, Label: "Plan", Required: true, Id: "s").ToHtml());

    [Fact]
    public void Select_Floating_WrapsBoxAndLabelInFormFloatingWithBlankBox() =>
        // Float-only-when-filled: the empty box carries no placeholder text (the label acts as the
        // placeholder), the wrapper is .form-floating.bs-floating, and .bs-floating-filled is absent.
        Assert.Contains(
            "<div class=\"form-floating bs-floating position-relative\">" +
            "<div id=\"s\" class=\"form-select\" data-rask-anchor=\"\" role=\"combobox\" tabindex=\"0\" " +
            "aria-haspopup=\"listbox\" aria-expanded=\"false\" aria-controls=\"s-list\"></div>" +
            "<label for=\"s\">Plan</label></div>",
            BsSelect<string>(Options: [], Value: null, Label: "Plan", Floating: true, Id: "s").ToHtml());

    [Fact]
    public void Select_FloatingWithValue_AddsFilledMarker() =>
        Assert.Contains(
            "<div class=\"form-floating bs-floating bs-floating-filled position-relative\">",
            BsSelect<string>(Options: ["a"], Value: "a", Label: "Plan", Floating: true, Id: "s").ToHtml());

    [Fact]
    public void Select_NullableWithValue_ShowsClearButtonAndPadsBox() =>
        // A nullable (Nullable<T>) select with a value shows the × clear button (btn-close) and pads the box.
        Assert.Contains(
            "<div id=\"s\" class=\"form-select bs-select-clearable\" data-rask-anchor=\"\" role=\"combobox\" " +
            "tabindex=\"0\" aria-haspopup=\"listbox\" aria-expanded=\"false\" aria-controls=\"s-list\">2</div>" +
            "<button class=\"btn-close position-absolute top-50 translate-middle-y bs-select-clear\" " +
            "aria-label=\"Clear\" type=\"button\"></button>",
            BsSelect<int?>(Options: new int?[] { 1, 2 }, Value: 2, Id: "s").ToHtml());

    [Fact]
    public void Select_NonNullable_HasNoClearButton() =>
        Assert.DoesNotContain("bs-select-clear",
            BsSelect<string>(Options: ["a"], Value: "a", Id: "s").ToHtml());

    [Fact]
    public void Select_Disabled_DropsInteractivityAndDisablesOptions() =>
        Assert.Equal(
            "<div class=\"dropdown\" data-rask-popover=\"\">" +
            "<div id=\"s\" class=\"form-select disabled pe-none\" data-rask-anchor=\"\" role=\"combobox\" " +
            "aria-haspopup=\"listbox\" aria-expanded=\"false\" aria-controls=\"s-list\">" +
            "<span class=\"text-secondary\">Select&#x2026;</span></div>" +
            "<div id=\"s-list\" class=\"dropdown-menu\" role=\"listbox\">" +
            "<button id=\"s-opt-0\" class=\"dropdown-item\" data-rask-key=\"0\" role=\"option\" type=\"button\" disabled>a</button>" +
            "</div></div>",
            BsSelect<string>(Options: ["a"], Value: null, Disabled: true, Id: "s").ToHtml());

    [Fact]
    public void Select_Native_RendersPlainSelectWithSelectedOptionAndPlaceholder() =>
        Assert.Equal(
            "<div><select id=\"t\" class=\"form-select\">" +
            "<option data-rask-key=\"placeholder\" value=\"\" disabled>Pick</option>" +
            "<option value=\"a\" selected>a</option>" +
            "<option data-rask-key=\"1\" value=\"b\">b</option>" +
            "</select></div>",
            BsSelect<string>(Options: ["a", "b"], Value: "a", Native: true, Placeholder: "Pick", Id: "t").ToHtml());
}
