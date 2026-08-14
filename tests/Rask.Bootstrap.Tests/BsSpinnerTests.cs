namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsSpinner — the .spinner-border/.spinner-grow classes, colour/size
// modifiers, and the visually-hidden status label emitted for assistive tech.
public partial class BsSpinnerTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Spinner_Default_IsBorderWithHiddenLabel() =>
        Assert.Equal(
            "<div class=\"spinner-border\" role=\"status\"><span class=\"visually-hidden\">Loading&#x2026;</span></div>",
            BsSpinner.ToHtml());

    [Fact]
    public void Spinner_Grow_SwitchesAnimationClass() =>
        Assert.Equal(
            "<div class=\"spinner-grow\" role=\"status\"><span class=\"visually-hidden\">Loading&#x2026;</span></div>",
            BsSpinner.Kind(BsSpinnerKind.Grow).ToHtml());

    [Fact]
    public void Spinner_SmallColored_StacksModifiers() =>
        Assert.Equal(
            "<div class=\"spinner-border spinner-border-sm text-primary\" role=\"status\">" +
            "<span class=\"visually-hidden\">Loading&#x2026;</span></div>",
            BsSpinner.Color(BsColor.Primary).Small(true).ToHtml());

    [Fact]
    public void Spinner_CustomLabel_ReplacesHiddenText() =>
        Assert.Equal(
            "<div class=\"spinner-border\" role=\"status\"><span class=\"visually-hidden\">Fetching</span></div>",
            BsSpinner.Label("Fetching").ToHtml());
}
