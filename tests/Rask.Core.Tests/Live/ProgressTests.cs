using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     The bar along the top of the viewport both hosts show while the reader waits: only once an answer has taken
///     longer than the delay, in the app's accent, and still for a reader who asked for less motion.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-progress.ts</c> on Jint against a stub page, on a clock turned by hand
///     (<c>ProgressFixture.ts</c>). The hosts' wiring is covered in a real browser by the navigation journeys.
/// </remarks>
public sealed class ProgressTests
{
    private static JsonElement Run() => ScriptFixture.Run("ProgressFixture");

    [Fact]
    public void The_bar_waits_150_ms_as_Livewire_measured()
    {
        var root = Run();

        var delay = root.GetProperty("delay").GetInt32();

        Assert.Equal(150, delay);
    }

    [Fact]
    public void An_answer_inside_the_delay_never_draws_the_bar()
    {
        var root = Run();

        var fast = root.GetProperty("fast");

        Assert.False(fast.GetProperty("before").GetBoolean());
        Assert.False(fast.GetProperty("never").GetBoolean());
        Assert.False(fast.GetProperty("installed").GetBoolean(), "the bar was put on the page for a wait it never showed");
    }

    [Fact]
    public void Two_quick_answers_that_overlap_draw_no_bar_though_the_page_was_busy_for_longer_than_the_delay()
    {
        var root = Run();

        var overlap = root.GetProperty("overlap");

        Assert.False(overlap.GetProperty("shown").GetBoolean());
    }

    [Fact]
    public void A_slow_answer_draws_the_bar_at_the_delay_and_it_goes_once_nothing_is_waited_on()
    {
        var root = Run();

        var slow = root.GetProperty("slow");

        Assert.False(slow.GetProperty("before").GetBoolean());
        Assert.True(slow.GetProperty("at").GetBoolean());
        Assert.True(slow.GetProperty("stillWaited").GetBoolean());
        Assert.False(slow.GetProperty("gone").GetBoolean());
    }

    [Fact]
    public void Answering_one_wait_twice_does_not_answer_another_so_the_bar_still_goes()
    {
        var root = Run();

        var slow = root.GetProperty("slow");

        Assert.True(slow.GetProperty("thirdShown").GetBoolean());
        Assert.False(slow.GetProperty("twice").GetBoolean());
    }

    [Fact]
    public void The_bar_is_hidden_from_assistive_tech_and_left_alone_by_the_morph()
    {
        var root = Run();

        var bar = root.GetProperty("bar");

        Assert.Equal("true", bar.GetProperty("hidden").GetString());
        Assert.True(bar.GetProperty("managed").GetBoolean());
    }

    [Fact]
    public void The_bar_is_drawn_in_the_apps_accent_with_Rasks_purple_where_there_is_none()
    {
        var root = Run();

        var css = root.GetProperty("css").GetString()!;

        Assert.Contains("var(--color-accent,#7C3AED)", css, StringComparison.Ordinal);
        Assert.DoesNotContain("rgba(124,58,237", css, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reader_who_asked_for_less_motion_sees_a_still_bar()
    {
        var root = Run();

        var css = root.GetProperty("css").GetString()!;

        var reduced = css[css.IndexOf("@media (prefers-reduced-motion:reduce)", StringComparison.Ordinal)..];
        Assert.Contains("animation:none", reduced, StringComparison.Ordinal);
        Assert.Contains("width:100%", reduced, StringComparison.Ordinal);
    }
}
