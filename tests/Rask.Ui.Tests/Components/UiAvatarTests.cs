namespace Rask.UiTests.Components;

/// <summary>
///     Flux's avatar: what it shows for a person, what it is written as, and the colour it picks.
/// </summary>
public partial class UiAvatarTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData("Caleb Porzio", "CP")]
    [InlineData("calebporzio", "Ca")]
    [InlineData("Ada Byron Lovelace", "AL")]
    [InlineData("  ada   lovelace ", "AL")]
    [InlineData("x", "X")]
    public void Initials_are_worked_out_from_the_name_as_Flux_does(string name, string expected)
    {
        var html = Ui.Avatar.Name(name).ToHtml();

        Assert.Contains($"<span class=\"select-none\">{expected}</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_initial_can_be_asked_for_and_stated_initials_win()
    {
        var single = Ui.Avatar.Name("calebporzio").InitialsSingle().ToHtml();

        var stated = Ui.Avatar.Name("Caleb Porzio").Initials("XY").ToHtml();

        Assert.Contains(">C</span>", single, StringComparison.Ordinal);
        Assert.Contains(">XY</span>", stated, StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_takes_its_alternative_text_from_the_name()
    {
        var named = Ui.Avatar.Name("Ada").Src("/me.png").ToHtml();

        var stated = Ui.Avatar.Name("Ada").Alt("Portrait of Ada").Src("/me.png").ToHtml();

        Assert.Contains("alt=\"Ada\"", named, StringComparison.Ordinal);
        Assert.Contains("alt=\"Portrait of Ada\"", stated, StringComparison.Ordinal);
        Assert.DoesNotContain("<span", named, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "md", "size-10")]
    [InlineData(Ui.AvatarSize.Xs, "xs", "size-6")]
    [InlineData(Ui.AvatarSize.Sm, "sm", "size-8")]
    [InlineData(Ui.AvatarSize.Lg, "lg", "size-12")]
    [InlineData(Ui.AvatarSize.Xl, "xl", "size-16")]
    public void Its_size_is_said_as_Flux_says_it(Ui.AvatarSize? size, string name, string expected)
    {
        var html = Ui.Avatar.Name("Ada").Size(size).ToHtml();

        Assert.Contains($"data-size=\"{name}\"", html, StringComparison.Ordinal);
        Assert.Contains(expected, html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_circle_is_marked_and_fully_rounded()
    {
        var html = Ui.Avatar.Name("Ada").Circle().ToHtml();

        Assert.Contains("data-circle=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("rounded-full", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CP", Ui.Color.Emerald)]
    [InlineData("MJ", Ui.Color.Sky)]
    [InlineData("KC", Ui.Color.Red)]
    [InlineData("KN", Ui.Color.Violet)]
    [InlineData("KS", Ui.Color.Fuchsia)]
    [InlineData("BP", Ui.Color.Lime)]
    [InlineData("AB", Ui.Color.Pink)]
    public void Auto_colour_picks_the_hue_Flux_picks_for_the_same_initials(string initials, Ui.Color expected)
    {
        var auto = Ui.Avatar.Initials(initials).ColorAuto().ToHtml();

        var stated = Ui.Avatar.Initials(initials).Color(expected).ToHtml();

        Assert.Equal(stated, auto);
    }

    [Fact]
    public void A_seed_decides_the_colour_instead_of_the_initials()
    {
        var first = Ui.Avatar.Name("Caleb Porzio").ColorAuto().ColorSeed("42").ToHtml();

        var second = Ui.Avatar.Name("Someone Else").Initials("CP").ColorAuto().ColorSeed("42").ToHtml();

        Assert.Equal(first, second);
        Assert.NotEqual(first, Ui.Avatar.Name("Caleb Porzio").ColorAuto().ToHtml());
    }

    [Fact]
    public void It_is_a_div_until_it_does_something()
    {
        var plain = Ui.Avatar.Name("Ada").ToHtml();
        var button = Ui.Avatar.Name("Ada").As(Ui.AvatarAs.Button).ToHtml();
        var link = Ui.Avatar.Name("Ada").Href("https://example.test/ada").ToHtml();

        Assert.StartsWith("<div ", plain, StringComparison.Ordinal);
        Assert.StartsWith("<button ", button, StringComparison.Ordinal);
        Assert.Contains("type=\"button\"", button, StringComparison.Ordinal);
        Assert.StartsWith("<a ", link, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.test/ada\"", link, StringComparison.Ordinal);
    }

    [Fact]
    public void An_icon_stands_in_for_initials_and_children_stand_in_for_both()
    {
        var icon = Ui.Avatar.Name("Ada").Icon(Ui.IconName.User).ToHtml();

        var own = Ui.Avatar.Name("Ada").Icon(Ui.IconName.User)["12"].ToHtml();

        Assert.Contains("<svg", icon, StringComparison.Ordinal);
        Assert.DoesNotContain("<span", icon, StringComparison.Ordinal);
        Assert.Contains(">12</span>", own, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", own, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_badge_is_the_dot_and_it_is_hidden_from_assistive_tech()
    {
        var html = Ui.Avatar.Name("Ada").Badge("").BadgeColor(Ui.Color.Green).ToHtml();

        Assert.Contains("aria-hidden=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("bg-green-500", html, StringComparison.Ordinal);
        Assert.Contains("end-0 bottom-0", html, StringComparison.Ordinal);
        Assert.Contains("rounded-[3px]", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_badge_can_move_corner_turn_round_and_be_outlined()
    {
        var html = Ui.Avatar.Name("Ada").Badge("").BadgeCircle().BadgePosition(Ui.AvatarBadgePosition.TopLeft)
            .BadgeVariant(Ui.AvatarBadgeVariant.Outline).ToHtml();

        Assert.Contains("start-0 top-0", html, StringComparison.Ordinal);
        Assert.Contains("rounded-full", html, StringComparison.Ordinal);
        Assert.Contains("after:inset-[3px]", html, StringComparison.Ordinal);
    }

    [Fact]
    public void No_badge_is_drawn_unless_one_is_given()
    {
        var html = Ui.Avatar.Name("Ada").BadgeColor(Ui.Color.Green).ToHtml();

        Assert.DoesNotContain("aria-hidden", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tooltip_wraps_the_avatar_in_the_kit_tooltip()
    {
        var html = Ui.Avatar.Name("Ada").Tooltip("Ada Lovelace").ToHtml();

        Assert.Contains("Ada Lovelace", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("data-ui-tooltip ", StringComparison.Ordinal) < html.IndexOf("data-ui-avatar", StringComparison.Ordinal), html);
    }

    [Fact]
    public void A_group_overlaps_its_avatars_and_rings_each_in_the_ground_colour()
    {
        var html = Ui.AvatarGroup.Class("*:ring-zinc-100")[Ui.Avatar.Name("Ada"), Ui.Avatar.Name("Bo")].ToHtml();

        Assert.Contains("*:not-first:-ms-2", html, StringComparison.Ordinal);
        Assert.Contains("*:ring-4", html, StringComparison.Ordinal);
        Assert.Contains("*:ring-zinc-100", html, StringComparison.Ordinal);
    }
}
