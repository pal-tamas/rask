namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsSelect (controlled mode). The custom box is a `.form-select` display
// combobox `<div>` (showing the selected option's rich label, or a placeholder) that opens a `.dropdown-menu`
// listbox of role=option buttons; supplying a Filter predicate adds a search field in the dropdown (only
// present while open, so not in this static closed markup). Native: true drops to the plain `<select>`.
public class BsSelectTests
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
        Assert.Contains("<label class=\"form-label\" for=\"p\">Plan</label>", html);
        Assert.Contains("<span class=\"text-secondary\">Pick one</span>", html);
    }

    [Fact]
    public void Select_Required_AppendsAsteriskToLabel() =>
        Assert.Contains(
            "<label class=\"form-label\" for=\"s\">Plan<span class=\"text-danger ms-1\">*</span></label>",
            BsSelect<string>(Options: [], Value: null, Label: "Plan", Required: true, Id: "s").ToHtml());

    [Fact]
    public void Select_Floating_WrapsBoxAndLabelInFormFloatingWithBlankBox()
    {
        // Float-only-when-filled: the empty box carries no placeholder text (the label is the placeholder),
        // the wrapper is .form-floating.bs-floating, and .bs-floating-filled is absent.
        var html = BsSelect<string>(Options: [], Value: null, Label: "Plan", Floating: true, Id: "s").ToHtml();
        Assert.Contains("<div class=\"form-floating bs-floating position-relative\">", html);
        Assert.Contains("aria-controls=\"s-list\"></div><label for=\"s\">Plan</label>", html);
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
            OptionLabel: t => Text(t.Name), Value: 2, Id: "s").ToHtml();
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
                OptionValue: t => t.Id, OptionLabel: t => Text(t.Name), Value: 1, Native: true, Id: "s").ToHtml());

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
