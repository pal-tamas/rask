namespace Rask.UiTests.Components;

/// <summary>
///     Flux's badge: the element it is, what each prop writes, where the icons go, and the close button.
/// </summary>
public partial class UiBadgeTests : global::Rask.Core.RaskMarkup
{
    private const string Shape = "inline-flex items-center font-medium whitespace-nowrap text-sm py-1 rounded-md px-2";

    private const string Zinc = "text-zinc-700 bg-zinc-400/15 dark:text-zinc-200 dark:bg-zinc-400/40";

    [Fact]
    public void A_badge_is_one_zinc_div_showing_its_children()
    {
        var html = Ui.Badge["Live"].ToHtml();

        Assert.Equal($"<div class=\"{Shape} {Zinc}\" data-ui-badge>Live</div>", html);
    }

    [Fact]
    public void Element_steps_reach_the_badge_and_keep_the_documented_attribute_order()
    {
        var html = Ui.Badge.Id("count").Class("ms-2").Data("testid", "count").Title("Unread")["9"].ToHtml();

        Assert.Equal(
            $"<div id=\"count\" class=\"{Shape} {Zinc} ms-2\" title=\"Unread\" data-ui-badge data-testid=\"count\">9</div>",
            html);
    }

    [Theory]
    [InlineData(Ui.BadgeSize.Base, "text-sm py-1 ")]
    [InlineData(Ui.BadgeSize.Sm, "text-xs py-1 ")]
    [InlineData(Ui.BadgeSize.Lg, "text-sm py-1.5 ")]
    public void A_size_sets_the_type_and_the_height(Ui.BadgeSize size, string classes)
    {
        var html = Ui.Badge.Size(size)["x"].ToHtml();

        Assert.Contains(classes, html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rounded_badge_has_round_ends_and_the_wider_padding_they_need()
    {
        var html = Ui.Badge.Rounded()["Users"].ToHtml();

        Assert.Contains("rounded-full px-3", html, StringComparison.Ordinal);
        Assert.DoesNotContain("rounded-md", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.Color.Red, "text-red-700 bg-red-400/20 dark:text-red-200 dark:bg-red-400/40")]
    [InlineData(Ui.Color.Amber, "text-amber-700 bg-amber-400/25 dark:text-amber-200 dark:bg-amber-400/40")]
    [InlineData(Ui.Color.Lime, "text-lime-800 bg-lime-400/25 dark:text-lime-200 dark:bg-lime-400/40")]
    [InlineData(Ui.Color.Blue, "text-blue-800 bg-blue-400/20 dark:text-blue-200 dark:bg-blue-400/40")]
    [InlineData(Ui.Color.Zinc, Zinc)]
    public void A_colour_tints_the_badge_and_darkens_its_text(Ui.Color color, string classes)
    {
        var html = Ui.Badge.Color(color)["x"].ToHtml();

        Assert.Contains(classes, html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.Color.Green, "text-white bg-green-500 dark:bg-green-600")]
    [InlineData(Ui.Color.Amber, "text-white bg-amber-500 dark:text-zinc-950")]
    [InlineData(Ui.Color.Yellow, "text-white bg-yellow-500 dark:text-zinc-950 dark:bg-yellow-400")]
    [InlineData(Ui.Color.Zinc, "text-white bg-zinc-600")]
    public void A_solid_badge_is_the_colour_itself_under_text_that_reads_on_it(Ui.Color color, string classes)
    {
        var html = Ui.Badge.Solid.Color(color)["x"].ToHtml();

        Assert.Contains(classes, html, StringComparison.Ordinal);
        Assert.DoesNotContain("/20", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_colour_is_drawn_in_both_variants()
    {
        var colors = Enum.GetValues<Ui.Color>();

        var drawn = colors.Select(color => (
            Name: color.ToString().ToLowerInvariant(),
            Soft: Ui.Badge.Color(color)["x"].ToHtml(),
            Solid: Ui.Badge.Solid.Color(color)["x"].ToHtml())).ToList();

        Assert.Equal(22, drawn.Count);
        Assert.All(drawn, badge => Assert.Contains($"bg-{badge.Name}-400/", badge.Soft, StringComparison.Ordinal));
        Assert.All(drawn, badge => Assert.Contains($"bg-{badge.Name}-", badge.Solid, StringComparison.Ordinal));
    }

    [Fact]
    public void An_icon_is_drawn_before_the_words_at_the_size_of_the_line()
    {
        var html = Ui.Badge.Icon(Ui.IconName.User)["Users"].ToHtml();

        Assert.Contains("data-ui-badge><svg class=\"shrink-0 [:where(&amp;)]:size-4 me-1.5\"", html, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 16 16\"", html, StringComparison.Ordinal);
        Assert.EndsWith("</svg>Users</div>", html, StringComparison.Ordinal);
        Assert.Contains(" data-ui-icon data-slot=\"icon\" aria-hidden=\"true\" data-ui-badge-icon", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trailing_icon_is_drawn_after_the_words_in_its_own_box()
    {
        var html = Ui.Badge.IconTrailing(Ui.IconName.VideoCamera)["Videos"].ToHtml();

        Assert.Contains("data-ui-badge>Videos<div class=\"flex items-center ps-1\" data-ui-badge-icon:trailing><svg ", html, StringComparison.Ordinal);
        Assert.EndsWith("</svg></div></div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_large_badge_gives_its_icon_more_room()
    {
        var html = Ui.Badge.Lg.Icon(Ui.IconName.Plus)["Amount"].ToHtml();

        Assert.Contains("size-4 me-2\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_icon_variant_picks_the_drawing_for_both_icons()
    {
        var html = Ui.Badge.Icon(Ui.IconName.User).IconTrailing(Ui.IconName.Plus).IconVariant(Ui.IconVariant.Mini)["x"].ToHtml();

        Assert.Equal(2, html.Split("viewBox=\"0 0 20 20\"").Length - 1);
        Assert.DoesNotContain("0 0 16 16", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Rendering_with_icons_leaves_the_call_sites_children_as_they_were()
    {
        // The icons stand around the children only while they are written, so a second render does not
        // wrap them twice.
        var badge = Ui.Badge.Icon(Ui.IconName.User)["Users"];

        var first = badge.ToHtml();
        var second = badge.ToHtml();

        Assert.Equal(first, second);
        Assert.Single(first.Split("<svg").Skip(1));
    }

    [Fact]
    public void A_badge_as_a_button_is_a_button_that_does_not_submit_and_deepens_under_the_pointer()
    {
        var html = Ui.Badge.As(Ui.BadgeAs.Button)["Amount"].ToHtml();

        Assert.StartsWith("<button class=\"", html, StringComparison.Ordinal);
        Assert.Contains("hover:bg-zinc-400/25 dark:hover:bg-zinc-400/50", html, StringComparison.Ordinal);
        Assert.Contains(" data-ui-badge type=\"button\">Amount</button>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_badge_that_is_not_pressed_does_not_react_to_the_pointer()
    {
        var html = Ui.Badge.Color(Ui.Color.Red)["Failed"].ToHtml();

        Assert.DoesNotContain("hover:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("type=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pressed_badge_takes_its_handler_from_the_element()
    {
        var badge = Ui.Badge.As(Ui.BadgeAs.Button);

        var wired = badge.OnClick(() => { });

        Assert.True(wired.OnClick.HasValue);
    }

    [Theory]
    [InlineData(Ui.Inset.Top | Ui.Inset.Bottom, "-mt-1 -mb-1")]
    [InlineData(Ui.Inset.Left, "-ms-2")]
    [InlineData(Ui.Inset.Right, "-me-2")]
    public void An_inset_takes_the_padding_back_on_the_sides_it_names(Ui.Inset inset, string classes)
    {
        var html = Ui.Badge.Inset(inset)["New"].ToHtml();

        Assert.Contains(classes, html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inset_follows_the_padding_of_a_large_rounded_badge()
    {
        var all = Ui.Inset.Top | Ui.Inset.Bottom | Ui.Inset.Left | Ui.Inset.Right;

        var html = Ui.Badge.Lg.Rounded().Inset(all)["New"].ToHtml();

        Assert.Contains("-mt-1.5 -mb-1.5 -ms-3 -me-3", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_close_button_is_a_button_showing_a_small_cross_and_no_name_of_its_own()
    {
        var html = Ui.BadgeClose.ToHtml();

        Assert.StartsWith(
            "<button class=\"p-1 -my-1 -me-1 opacity-50 hover:opacity-100\" data-ui-badge-close type=\"button\"><svg ",
            html,
            StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 16 16\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_close_button_keeps_the_name_the_call_site_gave_it()
    {
        var html = Ui.BadgeClose.AriaLabel("Remove Admin").ToHtml();

        Assert.Contains("aria-label=\"Remove Admin\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_close_button_can_show_another_icon()
    {
        var html = Ui.BadgeClose.Icon(Ui.IconName.Trash).IconVariant(Ui.IconVariant.Mini).ToHtml();

        Assert.Contains("viewBox=\"0 0 20 20\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_removable_badge_holds_its_close_button_after_its_words()
    {
        var html = Ui.Badge["Admin", Ui.BadgeClose].ToHtml();

        Assert.Contains("data-ui-badge>Admin<button ", html, StringComparison.Ordinal);
        Assert.EndsWith("</svg></button></div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_kit_writes_no_daisy_badge_class_any_more()
    {
        var kit = string.Concat(
            Ui.Badge.Color(Ui.Color.Green)["x"].ToHtml(),
            Ui.NavTab.Label("Errors").Href("/errors").Badge("12").BadgeTone(Ui.Tone.Error).ToHtml(),
            Ui.Select.Value("").Label("Email").Badge("Required").ToHtml());

        Assert.DoesNotContain("class=\"badge", kit, StringComparison.Ordinal);
        Assert.DoesNotContain(" badge-", kit, StringComparison.Ordinal);
        // The label's own word beside a field ("Required") is the label's, not a Ui.Badge: two badges, not three.
        Assert.Equal(2, kit.Split("data-ui-badge ").Length + kit.Split("data-ui-badge>").Length - 2);
    }
}
