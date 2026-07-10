namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsMultiSelect (controlled mode). The `.form-select` box shows the selected
// chips (BsBadge + BsCloseButton) or a placeholder; each option row is a dropdown-item button with a
// checkbox reflecting selection. Supplying a Filter predicate adds a search field in the dropdown (only
// present while open, so absent from this static closed markup). ToHtml() renders with the menu closed.
public class BsMultiSelectTests
{
    [Fact]
    public void MultiSelect_Empty_ShowsPlaceholderBoxAndUncheckedOptions()
    {
        var html = BsMultiSelect<string>(Options: ["a", "b"], Value: new List<string>()).ToHtml();
        Assert.Contains(
            "<div class=\"form-select h-auto d-flex flex-wrap align-items-center gap-1\" data-rask-anchor=\"\" " +
            "role=\"combobox\" tabindex=\"0\"><span class=\"text-secondary\">Select&#x2026;</span></div>", html);
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
        Assert.Contains("type=\"checkbox\" checked />a", html);
        Assert.Contains("type=\"checkbox\" checked />c", html);
    }

    [Fact]
    public void MultiSelect_Label_SitsAboveTheControl() =>
        Assert.Contains("<label class=\"form-label\">Tags</label>",
            BsMultiSelect<string>(Options: [], Value: new List<string>(), Label: "Tags").ToHtml());

    [Fact]
    public void MultiSelect_CustomPlaceholder_ReplacesDefault() =>
        Assert.Contains("<span class=\"text-secondary\">Pick tags</span>",
            BsMultiSelect<string>(Options: [], Value: new List<string>(), Placeholder: "Pick tags").ToHtml());

    [Fact]
    public void MultiSelect_FloatingEmpty_WrapsInFormFloatingWithBlankBox()
    {
        // Float-only-when-filled: the empty box carries NO "Select…" placeholder span (the centred floating
        // label is the placeholder) — otherwise the two texts overlap. Wrapper is .form-floating.bs-floating
        // with no .bs-floating-filled. Guards the regression where the leftover placeholder overlapped the label.
        var html = BsMultiSelect<string>(Options: [], Value: new List<string>(),
            Label: "Interests", Floating: true).ToHtml();
        Assert.Contains("<div class=\"form-floating bs-floating position-relative\">", html);
        Assert.Contains("role=\"combobox\" tabindex=\"0\"></div><label>Interests</label>", html);
        Assert.DoesNotContain("Select&#x2026;", html);
        Assert.DoesNotContain("bs-floating-filled", html);
    }

    [Fact]
    public void MultiSelect_FloatingWithChips_AddsFilledMarker()
    {
        var html = BsMultiSelect<string>(Options: ["a"], Value: new List<string> { "a" },
            Label: "Interests", Floating: true).ToHtml();
        Assert.Contains("<div class=\"form-floating bs-floating bs-floating-filled position-relative\">", html);
        Assert.DoesNotContain("Select&#x2026;", html);
    }

    [Fact]
    public void MultiSelect_RequiresExactlyOneOfBindOrValue() =>
        // Neither Bind nor Value set → the mode guard throws when the control renders.
        Assert.Throws<InvalidOperationException>(() => BsMultiSelect<string>(Options: ["a"]).ToHtml());
}
