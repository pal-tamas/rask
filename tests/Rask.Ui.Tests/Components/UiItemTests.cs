using Rask.Core;

namespace Rask.UiTests.Components;

/// <summary>
///     <see cref="UiItem" /> and its parts: Flux's markers on Flux's elements, the surface drawn once (by the group, or by
///     an item alone), the heading's level, and the narrow rule that moves a button with words under the text.
/// </summary>
public partial class UiItemTests : RaskMarkup
{
    [Fact]
    public void Each_part_writes_flux_s_marker_on_flux_s_element()
    {
        var item = Ui.Item[
            Ui.ItemMedia[Ui.Avatar.Initials("N")],
            Ui.ItemHeading["Northwind"],
            Ui.ItemSubheading["8 boards"],
            Ui.ItemActions[Ui.Badge.Sm["Member"]]
        ];

        var html = item.ToHtml().AsText();

        Assert.StartsWith("<div class=\"", html, StringComparison.Ordinal);
        Assert.Contains("\" data-ui-item><div class=\"", html, StringComparison.Ordinal);
        Assert.Contains("\" data-ui-item-media>", html, StringComparison.Ordinal);
        Assert.Matches("<div class=\"[^\"]*\" data-ui-item-heading data-ui-heading>Northwind</div>", html);
        Assert.Matches("<p class=\"[^\"]*\" data-ui-item-subheading>8 boards</p>", html);
        Assert.Contains("\" data-ui-item-actions>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_alone_draws_the_surface_and_one_in_a_group_draws_only_the_rule_under_it()
    {
        var alone = Ui.Item[Ui.ItemHeading["Sign out"]];
        var group = Ui.ItemGroup[Ui.Item[Ui.ItemHeading["Seats"]], Ui.Item[Ui.ItemHeading["Renew"]]];

        var single = alone.ToHtml().AsText();
        var grouped = group.ToHtml().AsText();

        Assert.Contains("border border-zinc-900/10 dark:border-white/10 [:where(&)]:rounded-xl shadow-xs", single, StringComparison.Ordinal);
        Assert.StartsWith("<div class=\"[:where(&)]:bg-white dark:bg-white/10 border border-zinc-900/10", grouped, StringComparison.Ordinal);
        Assert.Contains("data-ui-item-group", grouped, StringComparison.Ordinal);
        Assert.Equal(2, CountOf(grouped, "not-last:border-b border-zinc-900/5 dark:border-white/10\" data-ui-item>"));
        Assert.Equal(1, CountOf(grouped, "rounded-xl"));
    }

    [Fact]
    public void An_item_is_a_size_container_of_three_columns_at_least_a_small_button_tall()
    {
        var item = Ui.Item[Ui.ItemHeading["Seats"]];

        var html = item.ToHtml().AsText();

        Assert.Contains("[:where(&)]:grid grid-cols-[auto_1fr_auto] grid-rows-[1fr_auto_auto_auto_1fr] box-content [:where(&)]:min-h-8", html, StringComparison.Ordinal);
        Assert.Contains("@container", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "div")]
    [InlineData(2, "h2")]
    [InlineData(3, "h3")]
    [InlineData(6, "h6")]
    public void The_heading_renders_the_level_it_is_given(int? level, string tag)
    {
        var heading = Ui.ItemHeading.Level(level)["Language"];

        var html = heading.ToHtml().AsText();

        Assert.StartsWith($"<{tag} class=\"", html, StringComparison.Ordinal);
        Assert.EndsWith($">Language</{tag}>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_class_of_the_call_sites_is_added_to_each_part()
    {
        var item = Ui.ItemGroup.Class("max-w-xl")[Ui.Item.Class("bg-zinc-50")[Ui.ItemHeading.Class("truncate")["Seats"]]];

        var html = item.ToHtml().AsText();

        Assert.Contains("shadow-xs dark:shadow-none max-w-xl\"", html, StringComparison.Ordinal);
        Assert.Contains("dark:border-white/10 bg-zinc-50\"", html, StringComparison.Ordinal);
        Assert.Contains("dark:text-white truncate\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void On_a_narrow_item_actions_with_a_button_of_words_go_under_the_text_and_others_stay()
    {
        var actions = Ui.ItemActions[Ui.Button.Sm["Install"]];

        var html = actions.ToHtml().AsText();

        // Measured on fluxui.dev: a button moves them unless it is square or inside a dropdown, under 24rem of the item.
        const string Moves = "@max-sm:[&:has([data-ui-button]:not([data-ui-button-square],[data-ui-dropdown]_*))]:";
        Assert.Contains(Moves + "col-[2/-1]", html, StringComparison.Ordinal);
        Assert.Contains(Moves + "row-start-4", html, StringComparison.Ordinal);
        Assert.Contains(Moves + "mt-3", html, StringComparison.Ordinal);
        Assert.Contains(Moves + "ms-0", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_icon_button_says_it_is_square_as_flux_s_does_and_a_button_with_words_does_not()
    {
        var icon = Ui.Button.Sm.Ghost.Icon(Ui.IconName.EllipsisHorizontal);
        var words = Ui.Button.Sm["Install"];

        var square = icon.ToHtml().AsText();
        var labelled = words.ToHtml().AsText();

        Assert.Contains(" data-ui-button data-ui-button-square ", square, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-button-square", labelled, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.Inset.Right, "[:where(&)]:-me-2")]
    [InlineData(Ui.Inset.Left, "[:where(&)]:-ms-3")]
    public void An_inset_button_pulls_out_by_its_own_padding_on_that_side(Ui.Inset side, string margin)
    {
        var button = Ui.Button.Sm.Ghost.IconTrailing(Ui.IconName.ChevronDown).Inset(side)["English"];

        var html = button.ToHtml().AsText();

        // Measured on Flux: 8px beside the trailing chevron, 12px on the side of the words.
        Assert.Matches(System.Text.RegularExpressions.Regex.Escape(margin) + "[ \"]", html);
    }

    private static int CountOf(string html, string text)
    {
        var count = 0;
        for (var at = html.IndexOf(text, StringComparison.Ordinal); at >= 0; at = html.IndexOf(text, at + text.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
