namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsPlaceholder (Bootstrap loading skeletons).
public class BsPlaceholderTests
{
    [Fact]
    public void Placeholder_Col_SizesTheSpan() =>
        Assert.Equal(
            "<span class=\"placeholder col-6\"></span>",
            BsPlaceholder(Col: 6).ToHtml());

    [Fact]
    public void Placeholder_Color_AddsBackground() =>
        Assert.Equal(
            "<span class=\"placeholder bg-primary col-6\"></span>",
            BsPlaceholder(Col: 6, Color: BsColor.Primary).ToHtml());

    [Fact]
    public void Placeholder_Size_AddsSizeModifier() =>
        Assert.Equal(
            "<span class=\"placeholder placeholder-lg col-6\"></span>",
            BsPlaceholder(Col: 6, Size: BsSize.Lg).ToHtml());

    [Fact]
    // Md is the default scale and emits no placeholder-{size} class.
    public void Placeholder_MdSize_EmitsNoModifier() =>
        Assert.Equal(
            "<span class=\"placeholder col-6\"></span>",
            BsPlaceholder(Col: 6, Size: BsSize.Md).ToHtml());

    [Fact]
    public void Placeholder_NoAnimation_IsABareSpan() =>
        Assert.Equal(
            "<span class=\"placeholder col-4\"></span>",
            BsPlaceholder(Col: 4, Animation: BsPlaceholderAnimation.None).ToHtml());

    [Fact]
    public void Placeholder_Glow_WrapsInGlowSpan() =>
        Assert.Equal(
            "<span class=\"placeholder-glow\"><span class=\"placeholder col-8\"></span></span>",
            BsPlaceholder(Col: 8, Animation: BsPlaceholderAnimation.Glow).ToHtml());

    [Fact]
    public void Placeholder_Wave_WrapsInWaveSpan() =>
        Assert.Equal(
            "<span class=\"placeholder-wave\"><span class=\"placeholder col-8\"></span></span>",
            BsPlaceholder(Col: 8, Animation: BsPlaceholderAnimation.Wave).ToHtml());
}
