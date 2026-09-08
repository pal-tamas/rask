namespace Rask.Ui.Tests.Components;

public partial class UiAuraTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void It_wraps_its_children_in_the_base_class() =>
        Assert.Contains("class=\"aura\"", UiAura[Span["Pro"]].ToHtml());

    [Theory]
    [InlineData(UiAuraStyle.Glow, "aura-glow")]
    [InlineData(UiAuraStyle.Dual, "aura-dual")]
    [InlineData(UiAuraStyle.Holo, "aura-holo")]
    [InlineData(UiAuraStyle.Rainbow, "aura-rainbow")]
    [InlineData(UiAuraStyle.Gold, "aura-gold")]
    [InlineData(UiAuraStyle.Silver, "aura-silver")]
    public void Every_style_writes_its_own_class(UiAuraStyle style, string expected) =>
        Assert.Contains(expected, UiAura.Style(style).ToHtml());

    [Theory]
    [InlineData(UiSize.Xs, "aura-xs")]
    [InlineData(UiSize.Sm, "aura-sm")]
    [InlineData(UiSize.Md, "aura-md")]
    [InlineData(UiSize.Lg, "aura-lg")]
    [InlineData(UiSize.Xl, "aura-xl")]
    public void Every_size_writes_its_own_class(UiSize size, string expected) =>
        Assert.Contains(expected, UiAura.Size(size).ToHtml());

    [Fact]
    public void The_default_style_and_size_write_nothing_extra() =>
        Assert.Equal("<div class=\"aura\"></div>",
            UiAura.Style(UiAuraStyle.Default).Size(UiSize.Default).ToHtml());

    [Fact]
    public void It_says_nothing_to_a_screen_reader()
    {
        // Decoration. A reader who cannot see the glow loses nothing, so it carries no role and no
        // label — announcing "aura" would be noise standing between them and the content.
        var html = UiAura[Span["Pro"]].ToHtml();

        Assert.DoesNotContain("role=", html);
        Assert.DoesNotContain("aria-", html);
    }
}
