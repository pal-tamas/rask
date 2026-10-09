namespace Rask.UiTests.Components;

/// <summary>
///     Flux's text: a paragraph or a span, its size, and the ink a variant or a colour gives it.
/// </summary>
public partial class UiTextTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Text_is_a_marked_paragraph_in_body_ink()
    {
        var text = Ui.Text["Body"];

        var html = text.ToHtml();

        Assert.Equal("<p class=\"text-sm text-zinc-500 dark:text-white/70 [:where(&amp;)]:font-normal\" data-ui-text>Body</p>", html);
    }

    [Fact]
    public void Inline_text_is_a_span()
    {
        var text = Ui.Text.Inline()["run"];

        var html = text.ToHtml();

        Assert.StartsWith("<span ", html, StringComparison.Ordinal);
        Assert.EndsWith("</span>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.TextVariant.Default, "text-zinc-500 dark:text-white/70")]
    [InlineData(Ui.TextVariant.Strong, "text-zinc-800 dark:text-white")]
    [InlineData(Ui.TextVariant.Subtle, "text-zinc-400 dark:text-white/50")]
    public void A_variant_is_an_ink(Ui.TextVariant variant, string expected)
    {
        var text = Ui.Text.Variant(variant)["Body"];

        var html = text.ToHtml();

        Assert.Contains("\"text-sm " + expected + " ", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.TextSize.Sm, "text-xs")]
    [InlineData(Ui.TextSize.Default, "text-sm")]
    [InlineData(Ui.TextSize.Lg, "text-base")]
    [InlineData(Ui.TextSize.Xl, "text-lg")]
    public void A_size_is_one_step_of_the_scale(Ui.TextSize size, string expected)
    {
        var text = Ui.Text.Size(size)["Body"];

        var html = text.ToHtml();

        Assert.Contains("class=\"" + expected + " ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_colour_wins_over_a_variant_and_is_named_on_the_element()
    {
        var text = Ui.Text.Variant(Ui.TextVariant.Strong).Color(Ui.Color.Blue)["Colored text"];

        var html = text.ToHtml();

        Assert.Contains("text-blue-600 dark:text-blue-400", html, StringComparison.Ordinal);
        Assert.DoesNotContain("text-zinc-800", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-text data-color=\"blue\"", html, StringComparison.Ordinal);
    }

    // Flux's text takes the chromatic hues; Ui.Color lists them first, the five neutrals after.
    private static readonly Ui.Color[] Neutrals = [Ui.Color.Slate, Ui.Color.Gray, Ui.Color.Zinc, Ui.Color.Neutral, Ui.Color.Stone];

    [Fact]
    public void Every_chromatic_hue_has_an_ink_the_shipped_sheet_defines()
    {
        var hues = Enum.GetValues<Ui.Color>().Except(Neutrals).ToList();

        var missing = hues
            .Select(hue => Enum.GetName(hue)!.ToLowerInvariant())
            .Where(hue => !Ui.Text.Color(Enum.Parse<Ui.Color>(hue, ignoreCase: true))["x"].ToHtml()
                    .Contains($"text-{hue}-600 dark:text-{hue}-400", StringComparison.Ordinal)
                || !UiStylesheet.Css.Contains($".text-{hue}-600", StringComparison.Ordinal)
                || !UiStylesheet.Css.Contains($"text-{hue}-400", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(17, hues.Count);
        Assert.Empty(missing);
    }

    [Fact]
    public void A_neutral_colour_is_the_default_ink_as_it_is_in_Flux()
    {
        var plain = Ui.Text["x"].ToHtml();

        var neutral = Neutrals.Select(hue => Ui.Text.Color(hue)["x"].ToHtml()).ToList();

        Assert.All(neutral, html => Assert.Equal(plain, html));
    }
}
