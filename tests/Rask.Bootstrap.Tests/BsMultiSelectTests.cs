namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsMultiSelect (controlled mode). Chips reuse BsBadge + BsCloseButton;
// each option row is a dropdown-item button with a checkbox reflecting selection. ToHtml() renders
// static markup with the menu closed (the _open toggle is live-runtime view state).
public class BsMultiSelectTests
{
    [Fact]
    public void MultiSelect_Empty_ShowsPlaceholderAndUncheckedOptions() =>
        Assert.Equal(
            "<div class=\"dropdown\">" +
            "<div class=\"form-select h-auto d-flex flex-wrap align-items-center gap-1\" tabindex=\"0\">" +
            "<span class=\"text-secondary\">Select&#x2026;</span></div>" +
            "<div class=\"dropdown-menu\">" +
            "<button class=\"dropdown-item d-flex align-items-center gap-2\" data-rask-key=\"0\" type=\"button\">" +
            "<input class=\"form-check-input m-0 pe-none\" type=\"checkbox\" />a</button>" +
            "<button class=\"dropdown-item d-flex align-items-center gap-2\" data-rask-key=\"1\" type=\"button\">" +
            "<input class=\"form-check-input m-0 pe-none\" type=\"checkbox\" />b</button>" +
            "</div></div>",
            BsMultiSelect<string>(Options: ["a", "b"], Value: new List<string>()).ToHtml());

    [Fact]
    public void MultiSelect_WithSelection_RendersChipsAndChecksSelectedOptions() =>
        Assert.Equal(
            "<div class=\"dropdown\">" +
            "<div class=\"form-select h-auto d-flex flex-wrap align-items-center gap-1\" tabindex=\"0\">" +
            "<span class=\"badge text-bg-primary d-inline-flex align-items-center\" data-rask-key=\"0\">a" +
            "<button class=\"btn-close btn-close-white ms-1\" aria-label=\"Close\" type=\"button\"></button></span>" +
            "<span class=\"badge text-bg-primary d-inline-flex align-items-center\" data-rask-key=\"1\">c" +
            "<button class=\"btn-close btn-close-white ms-1\" aria-label=\"Close\" type=\"button\"></button></span>" +
            "</div>" +
            "<div class=\"dropdown-menu\">" +
            "<button class=\"dropdown-item d-flex align-items-center gap-2\" data-rask-key=\"0\" type=\"button\">" +
            "<input class=\"form-check-input m-0 pe-none\" type=\"checkbox\" checked />a</button>" +
            "<button class=\"dropdown-item d-flex align-items-center gap-2\" data-rask-key=\"1\" type=\"button\">" +
            "<input class=\"form-check-input m-0 pe-none\" type=\"checkbox\" />b</button>" +
            "<button class=\"dropdown-item d-flex align-items-center gap-2\" data-rask-key=\"2\" type=\"button\">" +
            "<input class=\"form-check-input m-0 pe-none\" type=\"checkbox\" checked />c</button>" +
            "</div></div>",
            BsMultiSelect<string>(Options: ["a", "b", "c"], Value: new List<string> { "a", "c" }).ToHtml());

    [Fact]
    public void MultiSelect_Label_SitsAboveTheControl() =>
        Assert.Equal(
            "<div class=\"dropdown\">" +
            "<label class=\"form-label\">Tags</label>" +
            "<div class=\"form-select h-auto d-flex flex-wrap align-items-center gap-1\" tabindex=\"0\">" +
            "<span class=\"text-secondary\">Select&#x2026;</span></div>" +
            "<div class=\"dropdown-menu\"></div></div>",
            BsMultiSelect<string>(Options: [], Value: new List<string>(), Label: "Tags").ToHtml());

    [Fact]
    public void MultiSelect_CustomPlaceholder_ReplacesDefault() =>
        Assert.Equal(
            "<div class=\"dropdown\">" +
            "<div class=\"form-select h-auto d-flex flex-wrap align-items-center gap-1\" tabindex=\"0\">" +
            "<span class=\"text-secondary\">Pick tags</span></div>" +
            "<div class=\"dropdown-menu\"></div></div>",
            BsMultiSelect<string>(Options: [], Value: new List<string>(), Placeholder: "Pick tags").ToHtml());

    [Fact]
    public void MultiSelect_RequiresExactlyOneOfBindOrValue() =>
        // Neither Bind nor Value set → the mode guard throws when the control renders.
        Assert.Throws<InvalidOperationException>(() => BsMultiSelect<string>(Options: ["a"]).ToHtml());
}
