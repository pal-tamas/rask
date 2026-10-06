using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's icon: what each variant writes, how it is sized, the spinner, and the generated tables behind it.
/// </summary>
public partial class UiIconTests : global::Rask.Core.RaskMarkup
{
    private const string Marks = " data-ui-icon data-slot=\"icon\" aria-hidden=\"true\"";

    [Fact]
    public void An_outline_icon_is_a_24px_drawing_stroked_in_the_text_colour()
    {
        var html = Ui.Icon.Name(Ui.IconName.Bolt).ToHtml();

        Assert.Equal(
            "<svg class=\"shrink-0 [:where(&amp;)]:size-6\"" + Marks
            + " fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\" viewBox=\"0 0 24 24\">"
            + "<path stroke-linecap=\"round\" stroke-linejoin=\"round\""
            + " d=\"m3.75 13.5 10.5-11.25L12 10.5h8.25L9.75 21.75 12 13.5H3.75Z\"></path></svg>",
            html);
    }

    [Theory]
    [InlineData(Ui.IconVariant.Solid, "size-6", "0 0 24 24")]
    [InlineData(Ui.IconVariant.Mini, "size-5", "0 0 20 20")]
    [InlineData(Ui.IconVariant.Micro, "size-4", "0 0 16 16")]
    public void A_filled_variant_is_drawn_for_its_own_size_and_carries_no_stroke(Ui.IconVariant variant, string size, string viewBox)
    {
        var html = Ui.Icon.Name(Ui.IconName.Bolt).Variant(variant).ToHtml();

        Assert.StartsWith(
            $"<svg class=\"shrink-0 [:where(&amp;)]:{size}\"{Marks} fill=\"currentColor\" viewBox=\"{viewBox}\"><path ",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("stroke", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_variant_is_a_step_and_outline_is_the_one_taken_when_none_is()
    {
        var bare = Ui.Icon.Name(Ui.IconName.Bolt).ToHtml();

        Assert.Equal(bare, Ui.Icon.Name(Ui.IconName.Bolt).Outline.ToHtml());
        Assert.Equal(Ui.Icon.Name(Ui.IconName.Bolt).Variant(Ui.IconVariant.Solid).ToHtml(), Ui.Icon.Name(Ui.IconName.Bolt).Solid.ToHtml());
        Assert.Contains("viewBox=\"0 0 20 20\"", Ui.Icon.Name(Ui.IconName.Bolt).Mini.ToHtml(), StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 16 16\"", Ui.Icon.Name(Ui.IconName.Bolt).Micro.ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_filled_by_the_even_odd_rule_says_so()
    {
        var html = Ui.Icon.Name(Ui.IconName.Bolt).Solid.ToHtml();

        Assert.Contains("<path fill-rule=\"evenodd\" clip-rule=\"evenodd\" d=\"M14.615 1.595", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_drawings_Heroicons_marks_up_differently_keep_their_markup()
    {
        var bold = Ui.Icon.Name(Ui.IconName.Bold).ToHtml();
        var stop = Ui.Icon.Name(Ui.IconName.Stop).Micro.ToHtml();

        Assert.Contains("<path stroke-linejoin=\"round\" d=\"M6.75 3.744", bold, StringComparison.Ordinal);
        Assert.DoesNotContain("stroke-linecap", bold, StringComparison.Ordinal);
        Assert.EndsWith("<rect x=\"3\" y=\"3\" width=\"10\" height=\"10\" rx=\"1.5\"></rect></svg>", stop, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("size-12")]
    [InlineData("text-amber-500 dark:text-amber-300")]
    [InlineData("me-1")]
    public void Call_site_classes_follow_the_icons_own(string extra)
    {
        var html = Ui.Icon.Name(Ui.IconName.Bolt).Class(extra).ToHtml();

        Assert.Contains($"class=\"shrink-0 [:where(&amp;)]:size-6 {extra}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_size_weighs_nothing_so_a_size_from_the_call_site_wins()
    {
        // Two size utilities on one element are settled by the order of the stylesheet, not of the class
        // attribute. Flux's answer, and this one: the default sits in :where(), which any class outranks.
        var css = UiStylesheet.Css;

        foreach (var size in new[] { "4", "5", "6" })
        {
            Assert.Contains($":where(.\\[\\:where\\(\\&\\)\\]\\:size-{size}){{width:calc(var(--spacing) * {size})", css, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_loading_icon_is_Fluxs_spinner_and_spins()
    {
        var html = Ui.Icon.Name(Ui.IconName.Loading).ToHtml();

        Assert.Equal(
            "<svg class=\"shrink-0 [:where(&amp;)]:size-6 animate-spin\"" + Marks + " fill=\"none\" viewBox=\"0 0 24 24\">"
            + "<circle class=\"opacity-25\" stroke=\"currentColor\" stroke-width=\"4\" cx=\"12\" cy=\"12\" r=\"10\"></circle>"
            + "<path class=\"opacity-75\" fill=\"currentColor\""
            + " d=\"M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z\"></path>"
            + "</svg>",
            html);
        Assert.Matches(new Regex(@"\.animate-spin\{animation:var\(--animate-spin\)\}"), UiStylesheet.Css);
        Assert.Matches(new Regex(@"--animate-spin:\s*spin 1s linear infinite"), UiStylesheet.Css);
        Assert.Matches(new Regex(@"@keyframes spin\{to\{transform:rotate\(360deg\)\}\}"), UiStylesheet.Css);
    }

    [Theory]
    [InlineData(Ui.IconVariant.Outline, "size-6")]
    [InlineData(Ui.IconVariant.Solid, "size-6")]
    [InlineData(Ui.IconVariant.Mini, "size-5")]
    [InlineData(Ui.IconVariant.Micro, "size-4")]
    public void The_spinner_is_one_drawing_that_a_variant_only_sizes(Ui.IconVariant variant, string size)
    {
        var html = Ui.Icon.Name(Ui.IconName.Loading).Variant(variant).ToHtml();

        Assert.Contains($"class=\"shrink-0 [:where(&amp;)]:{size} animate-spin\"", html, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 24 24\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_names_and_the_four_tables_agree()
    {
        var names = Enum.GetValues<Ui.IconName>();

        Assert.Equal(UiIconPaths.Count + 1, names.Length);
        Assert.Equal(Ui.IconName.Loading, names[^1]);
        Assert.Equal(UiIconPaths.Count, (int)Ui.IconName.Loading);
        AssertTable(UiIconPaths.OutlineData, UiIconPaths.OutlineOffsets);
        AssertTable(UiIconPaths.SolidData, UiIconPaths.SolidOffsets);
        AssertTable(UiIconPaths.MiniData, UiIconPaths.MiniOffsets);
        AssertTable(UiIconPaths.MicroData, UiIconPaths.MicroOffsets);
    }

    [Fact]
    public void Every_name_draws_something_in_every_variant()
    {
        var blank = new List<string>();

        foreach (var name in Enum.GetValues<Ui.IconName>())
        {
            foreach (var variant in Enum.GetValues<Ui.IconVariant>())
            {
                var html = Ui.Icon.Name(name).Variant(variant).ToHtml();
                if (!Regex.IsMatch(html, "<(path|rect) [^>]*(d=\"[Mm]|rx=)"))
                {
                    blank.Add($"{name}/{variant}");
                }
            }
        }

        Assert.Empty(blank);
    }

    [Fact]
    public void A_name_is_Heroicons_own_in_PascalCase()
    {
        var names = Enum.GetNames<Ui.IconName>();

        Assert.Contains(nameof(Ui.IconName.ArrowDownTray), names);
        Assert.Contains(nameof(Ui.IconName.XMark), names);
        Assert.Contains(nameof(Ui.IconName.Bars3BottomLeft), names);
    }

    private static void AssertTable(ReadOnlySpan<byte> data, ReadOnlySpan<int> offsets)
    {
        Assert.Equal(UiIconPaths.Count + 1, offsets.Length);
        Assert.Equal(0, offsets[0]);
        Assert.Equal(data.Length, offsets[^1]);
        for (var i = 0; i < UiIconPaths.Count; i++)
        {
            Assert.True(offsets[i] < offsets[i + 1], $"Icon {(Ui.IconName)i} is empty.");
        }
    }
}
