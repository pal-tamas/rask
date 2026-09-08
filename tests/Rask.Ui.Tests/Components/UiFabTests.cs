namespace Rask.Ui.Tests.Components;

/// <summary>
///     The floating action button, which the browser opens rather than C#.
/// </summary>
public partial class UiFabTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_trigger_is_the_first_child_and_carries_a_tabindex()
    {
        // Both halves are load-bearing. daisyUI selects the trigger as `[tabindex]:first-child`, and
        // `:focus-within` on the container is the ONLY thing that reveals the actions — there is no
        // `fab-open` class, so a trigger that cannot take focus is a FAB that cannot open.
        var html = UiFab.AccessibleLabel("Compose").ToHtml();

        Assert.Contains("tabindex=\"0\"", html);
        Assert.Contains("role=\"button\"", html);
    }

    [Fact]
    public void It_names_itself() =>
        Assert.Contains("aria-label=\"Compose\"", UiFab.AccessibleLabel("Compose").ToHtml());

    [Fact]
    public void The_flower_layout_is_opt_in()
    {
        Assert.DoesNotContain("fab-flower", UiFab.AccessibleLabel("Compose").ToHtml());
        Assert.Contains("fab-flower", UiFab.AccessibleLabel("Compose").Flower(true).ToHtml());
    }

    [Fact]
    public void A_main_action_is_drawn_over_the_trigger() =>
        Assert.Contains("fab-main-action",
            UiFab.AccessibleLabel("Compose").MainAction(Span["New note"]).ToHtml());

    [Fact]
    public void A_close_is_drawn_over_the_trigger() =>
        Assert.Contains("fab-close", UiFab.AccessibleLabel("Compose").Close(Span["x"]).ToHtml());

    [Fact]
    public void Neither_overlay_exists_unless_asked_for()
    {
        // daisyUI branches on `:has(.fab-main-action, .fab-close)`, so an empty wrapper rendered "just
        // in case" would change the trigger's behaviour for callers that supplied neither.
        var html = UiFab.AccessibleLabel("Compose").ToHtml();

        Assert.DoesNotContain("fab-main-action", html);
        Assert.DoesNotContain("fab-close", html);
    }

    [Fact]
    public void The_actions_are_always_rendered_because_CSS_is_what_hides_them()
    {
        // Rendering them conditionally would not work: they are `visibility: hidden` until focus lands
        // inside, so a C# flag saying "open" would leave them invisible and the two would disagree.
        Assert.Contains("Photo", UiFab.AccessibleLabel("Compose")[Span["Photo"]].ToHtml());
    }
}
