namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.Skeleton</c>, <c>Ui.SkeletonLine</c> and <c>Ui.SkeletonGroup</c>: Flux UI's skeleton, part by part.
/// </summary>
public partial class UiSkeletonTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_skeleton_is_a_still_bar_until_it_is_told_to_move()
    {
        var html = Ui.Skeleton.ToHtml();

        Assert.Contains(" data-ui-skeleton=\"\"", html, StringComparison.Ordinal);
        Assert.Contains("bg-zinc-400/20", html, StringComparison.Ordinal);
        Assert.DoesNotContain("animate", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_skeleton_gives_way_to_the_size_and_radius_the_call_site_writes()
    {
        var html = Ui.Skeleton.Class("size-10 rounded-full").Style("width:60%").ToHtml();

        // Zero specificity on the kit's own, so `size-10` and `rounded-full` win wherever their sheet lands.
        Assert.Contains("[:where(&amp;)]:h-4 [:where(&amp;)]:rounded-md", html, StringComparison.Ordinal);
        Assert.Contains("size-10 rounded-full\"", html, StringComparison.Ordinal);
        Assert.Contains("style=\"width:60%\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shimmer_is_a_band_of_light_carried_across_by_the_kit_keyframe()
    {
        var html = Ui.Skeleton.Shimmer.ToHtml();

        Assert.Contains("relative overflow-hidden", html, StringComparison.Ordinal);
        Assert.Contains("before:animate-[ui-shimmer_2s_infinite]", html, StringComparison.Ordinal);
        Assert.Contains("@keyframes ui-shimmer", UiStylesheet.Css, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pulse_is_the_whole_skeleton_fading()
    {
        var html = Ui.Skeleton.Pulse.ToHtml();

        Assert.Contains("animate-pulse", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ui-shimmer", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_keeps_the_height_of_the_text_and_draws_a_bar_the_height_of_its_letters()
    {
        var line = Ui.SkeletonLine.ToHtml();
        var large = Ui.SkeletonLine.Lg.ToHtml();

        Assert.Equal("<div class=\"py-[3px]\" data-ui-skeleton-line=\"\"><div class=\"h-3.5 rounded-sm bg-zinc-400/20\"></div></div>", line);
        Assert.Equal("<div class=\"py-1\" data-ui-skeleton-line=\"\"><div class=\"h-4 rounded-sm bg-zinc-400/20\"></div></div>", large);
    }

    [Fact]
    public void A_group_draws_nothing_and_animates_every_skeleton_inside_it()
    {
        var html = Ui.SkeletonGroup.Shimmer.Class("flex gap-4")[
            Ui.Skeleton,
            Div[Ui.SkeletonLine]
        ].ToHtml();

        Assert.StartsWith("<div class=\"flex gap-4\" data-ui-skeleton-group=\"\">", html, StringComparison.Ordinal);
        Assert.Equal(2, html.Split("before:animate-[ui-shimmer_2s_infinite]").Length - 1);
    }

    [Fact]
    public void A_skeleton_that_states_its_own_animation_does_not_take_the_group_one()
    {
        var html = Ui.SkeletonGroup.Shimmer[
            Ui.Skeleton.Pulse,
            Ui.SkeletonLine.Animate(Ui.SkeletonAnimate.None)
        ].ToHtml();

        Assert.Contains("animate-pulse", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ui-shimmer", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_with_no_animation_leaves_its_skeletons_still()
    {
        var html = Ui.SkeletonGroup[Ui.Skeleton, Ui.SkeletonLine].ToHtml();

        Assert.Contains("data-ui-skeleton-group", html, StringComparison.Ordinal);
        Assert.DoesNotContain("animate", html, StringComparison.Ordinal);
    }
}
