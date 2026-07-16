namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsBadge — the .badge span, Bootstrap 5.3's contrast-aware text-bg-*
// colour helper, the pill modifier, and user class/style merging.
public class BsBadgeTests
{
    [Fact]
    public void Badge_NoColor_IsPlainBadgeSpan() =>
        Assert.Equal(
            "<span class=\"badge\">New</span>",
            BsBadge()["New"].ToHtml());

    [Fact]
    public void Badge_Color_UsesContrastAwareTextBg() =>
        Assert.Equal(
            "<span class=\"badge text-bg-success\">New</span>",
            BsBadge(Color: BsColor.Success)["New"].ToHtml());

    [Fact]
    // Modifier order: base, colour, pill, then the caller's own class.
    public void Badge_PillWithColor_AddsRoundedPill() =>
        Assert.Equal(
            "<span class=\"badge text-bg-primary rounded-pill\">4</span>",
            BsBadge(Color: BsColor.Primary, Pill: true)["4"].ToHtml());

    [Fact]
    public void Badge_MergesUserClassAndStyle() =>
        Assert.Equal(
            "<span class=\"badge ms-1\" style=\"font-size:1rem\">7</span>",
            BsBadge(Class: "ms-1", Style: "font-size:1rem")["7"].ToHtml());
}
