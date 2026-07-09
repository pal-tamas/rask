namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsMultiSelect (controlled mode). The .form-select box holds the selected
// chips (BsBadge + BsCloseButton) followed by an inline .bs-multiselect-search <input> that filters the
// options as you type; each option row is a dropdown-item button with a checkbox reflecting selection.
// ToHtml() renders static markup with the menu closed and no active filter (view state is live-runtime).
public class BsMultiSelectTests
{
    [Fact]
    public void MultiSelect_Empty_ShowsSearchInputWithPlaceholderAndUncheckedOptions()
    {
        var html = BsMultiSelect<string>(Options: ["a", "b"], Value: new List<string>()).ToHtml();
        Assert.Contains(
            "<div class=\"form-select h-auto d-flex flex-wrap align-items-center gap-1\" data-rask-anchor=\"\">" +
            "<input class=\"bs-multiselect-search flex-grow-1\" aria-label=\"Search\" type=\"text\" value=\"\" " +
            "placeholder=\"Select&#x2026;\" autocomplete=\"off\" /></div>", html);
        Assert.Contains(
            "<button class=\"dropdown-item d-flex align-items-center gap-2\" data-rask-key=\"0\" type=\"button\">" +
            "<input class=\"form-check-input m-0 pe-none\" type=\"checkbox\" />a</button>", html);
    }

    [Fact]
    public void MultiSelect_WithSelection_RendersChipsAndChecksSelectedOptions()
    {
        var html = BsMultiSelect<string>(Options: ["a", "b", "c"], Value: new List<string> { "a", "c" }).ToHtml();
        Assert.Contains(
            "<span class=\"badge text-bg-primary d-inline-flex align-items-center\" data-rask-key=\"0\">a" +
            "<button class=\"btn-close btn-close-white ms-1\" aria-label=\"Close\" type=\"button\"></button></span>", html);
        // With chips present the search input drops its placeholder.
        Assert.Contains("<input class=\"bs-multiselect-search flex-grow-1\" aria-label=\"Search\" type=\"text\" value=\"\" autocomplete=\"off\" />", html);
        Assert.Contains("type=\"checkbox\" checked />a", html);
        Assert.Contains("type=\"checkbox\" checked />c", html);
    }

    [Fact]
    public void MultiSelect_Label_SitsAboveTheControl() =>
        Assert.Contains("<label class=\"form-label\">Tags</label>",
            BsMultiSelect<string>(Options: [], Value: new List<string>(), Label: "Tags").ToHtml());

    [Fact]
    public void MultiSelect_CustomPlaceholder_ReplacesDefault() =>
        Assert.Contains("placeholder=\"Pick tags\"",
            BsMultiSelect<string>(Options: [], Value: new List<string>(), Placeholder: "Pick tags").ToHtml());

    [Fact]
    public void MultiSelect_RequiresExactlyOneOfBindOrValue() =>
        // Neither Bind nor Value set → the mode guard throws when the control renders.
        Assert.Throws<InvalidOperationException>(() => BsMultiSelect<string>(Options: ["a"]).ToHtml());
}
