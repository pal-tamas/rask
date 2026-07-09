namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsSelect (controlled mode). By default it renders a searchable custom
// combobox — a .form-select-styled <input> that opens a .dropdown-menu listbox of role=option buttons
// (typing filters them); Native: true drops to the plain native <select>. ToHtml() renders static markup
// with the menu closed and no active filter, so an explicit Id keeps the option ids deterministic.
public class BsSelectTests
{
    [Fact]
    public void Select_Empty_RendersComboboxInputAndOptionButtons()
    {
        var html = BsSelect<string>(Options: ["a", "b"], Value: null, Id: "s").ToHtml();
        Assert.Contains(
            "<input id=\"s\" class=\"form-select\" data-rask-anchor=\"\" role=\"combobox\" " +
            "aria-haspopup=\"listbox\" aria-expanded=\"false\" aria-controls=\"s-list\" aria-autocomplete=\"list\" " +
            "type=\"text\" value=\"\" placeholder=\"Select&#x2026;\" autocomplete=\"off\" />", html);
        Assert.Contains(
            "<div id=\"s-list\" class=\"dropdown-menu\" role=\"listbox\">" +
            "<button id=\"s-opt-0\" class=\"dropdown-item\" data-rask-key=\"0\" role=\"option\" type=\"button\">a</button>" +
            "<button id=\"s-opt-1\" class=\"dropdown-item\" data-rask-key=\"1\" role=\"option\" type=\"button\">b</button>",
            html);
    }

    [Fact]
    public void Select_WithSelection_ShowsValueTextInInputAndMarksActiveOption()
    {
        var html = BsSelect<string>(Options: ["a", "b", "c"], Value: "b", Id: "s").ToHtml();
        Assert.Contains("type=\"text\" value=\"b\"", html);
        Assert.Contains(
            "<button id=\"s-opt-1\" class=\"dropdown-item active\" data-rask-key=\"1\" role=\"option\" " +
            "aria-selected=\"true\" type=\"button\">b</button>", html);
    }

    [Fact]
    public void Select_CustomPlaceholderAndLabel_LabelSitsAboveTheBox()
    {
        var html = BsSelect<string>(Options: [], Value: null, Label: "Plan", Placeholder: "Pick one", Id: "p").ToHtml();
        Assert.Contains("<label class=\"form-label\" for=\"p\">Plan</label>", html);
        Assert.Contains("placeholder=\"Pick one\"", html);
    }

    [Fact]
    public void Select_Required_AppendsAsteriskToLabel() =>
        Assert.Contains(
            "<label class=\"form-label\" for=\"s\">Plan<span class=\"text-danger ms-1\">*</span></label>",
            BsSelect<string>(Options: [], Value: null, Label: "Plan", Required: true, Id: "s").ToHtml());

    [Fact]
    public void Select_Floating_WrapsInputAndLabelInFormFloatingWithNoPlaceholder()
    {
        // Float-only-when-filled: the empty input carries no placeholder attr (the label is the placeholder),
        // the wrapper is .form-floating.bs-floating, and .bs-floating-filled is absent.
        var html = BsSelect<string>(Options: [], Value: null, Label: "Plan", Floating: true, Id: "s").ToHtml();
        Assert.Contains("<div class=\"form-floating bs-floating position-relative\">", html);
        Assert.DoesNotContain("placeholder=", html);
        Assert.Contains("<label for=\"s\">Plan</label>", html);
    }

    [Fact]
    public void Select_FloatingWithValue_AddsFilledMarker() =>
        Assert.Contains(
            "<div class=\"form-floating bs-floating bs-floating-filled position-relative\">",
            BsSelect<string>(Options: ["a"], Value: "a", Label: "Plan", Floating: true, Id: "s").ToHtml());

    [Fact]
    public void Select_Disabled_DisablesInputAndOptions()
    {
        var html = BsSelect<string>(Options: ["a"], Value: null, Disabled: true, Id: "s").ToHtml();
        Assert.Contains("<input id=\"s\" class=\"form-select\"", html);
        Assert.Contains("placeholder=\"Select&#x2026;\" disabled autocomplete=\"off\" />", html);
        Assert.Contains("role=\"option\" type=\"button\" disabled>a</button>", html);
    }

    [Fact]
    public void Select_NullableWithValue_ShowsClearButtonAndPadsBox()
    {
        // A nullable (Nullable<T>) select with a value pads the input and adds the × clear (btn-close).
        var html = BsSelect<int?>(Options: new int?[] { 1, 2 }, Value: 2, Id: "s").ToHtml();
        Assert.Contains("<input id=\"s\" class=\"form-select bs-select-clearable\"", html);
        Assert.Contains(
            "<button class=\"btn-close position-absolute top-50 translate-middle-y bs-select-clear\" " +
            "aria-label=\"Clear\" type=\"button\"></button>", html);
    }

    [Fact]
    public void Select_NonNullable_HasNoClearButton() =>
        Assert.DoesNotContain("bs-select-clear",
            BsSelect<string>(Options: ["a"], Value: "a", Id: "s").ToHtml());

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
