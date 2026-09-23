namespace Rask.UiTests.Components;

public partial class UiAuraTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void It_wraps_its_children_in_the_base_class() =>
        Assert.Contains("class=\"aura\"", Ui.Aura[Span["Pro"]].ToHtml());

    [Theory]
    [InlineData(Ui.AuraStyle.Glow, "aura-glow")]
    [InlineData(Ui.AuraStyle.Dual, "aura-dual")]
    [InlineData(Ui.AuraStyle.Holo, "aura-holo")]
    [InlineData(Ui.AuraStyle.Rainbow, "aura-rainbow")]
    [InlineData(Ui.AuraStyle.Gold, "aura-gold")]
    [InlineData(Ui.AuraStyle.Silver, "aura-silver")]
    public void Every_style_writes_its_own_class(Ui.AuraStyle style, string expected) =>
        Assert.Contains(expected, Ui.Aura.Style(style).ToHtml());

    [Theory]
    [InlineData(Ui.Size.Xs, "aura-xs")]
    [InlineData(Ui.Size.Sm, "aura-sm")]
    [InlineData(Ui.Size.Md, "aura-md")]
    [InlineData(Ui.Size.Lg, "aura-lg")]
    [InlineData(Ui.Size.Xl, "aura-xl")]
    public void Every_size_writes_its_own_class(Ui.Size size, string expected) =>
        Assert.Contains(expected, Ui.Aura.Size(size).ToHtml());

    [Fact]
    public void The_default_style_and_size_write_nothing_extra() =>
        Assert.Equal("<div class=\"aura\"></div>",
            Ui.Aura.Style(Ui.AuraStyle.Default).Size(Ui.Size.Default).ToHtml());

    [Fact]
    public void It_says_nothing_to_a_screen_reader()
    {
        // Decoration. A reader who cannot see the glow loses nothing, so it carries no role and no
        // label — announcing "aura" would be noise standing between them and the content.
        var html = Ui.Aura[Span["Pro"]].ToHtml();

        Assert.DoesNotContain("role=", html);
        Assert.DoesNotContain("aria-", html);
    }
}
