using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     The mark a field carries while the event that sent its value is unanswered: <c>data-loading</c>, as Livewire
///     puts it on any element that triggered a request, and no <c>aria-busy</c>.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-loading.ts</c> in-process on Jint with a fake clock (<c>FieldLoadingFixture.ts</c>).
///     The hosts' release — the ack, the dispatch promise — is <c>FieldBusyMarkTests</c> in
///     <c>Rask.Server.E2E.Tests</c> and <c>FieldBusyMarkWasmTests</c> in <c>Rask.Site.E2E.Tests</c>.
/// </remarks>
public sealed class FieldLoadingTests
{
    private static JsonElement Run() => ScriptFixture.Run("FieldLoadingFixture");

    [Fact]
    public void An_input_or_change_that_was_sent_names_its_field_and_nothing_else_does()
    {
        var root = Run();

        var target = root.GetProperty("target");

        Assert.True(target.GetProperty("input").GetBoolean());
        Assert.True(target.GetProperty("change").GetBoolean());
        Assert.True(target.GetProperty("click").GetBoolean());
        Assert.True(target.GetProperty("unasked").GetBoolean());
        Assert.True(target.GetProperty("gone").GetBoolean());
        Assert.True(target.GetProperty("optedOut").GetBoolean());
    }

    [Fact]
    public void A_search_sent_at_its_pause_names_the_field_that_waits_on_the_answer()
    {
        var root = Run();

        var target = root.GetProperty("target");

        Assert.True(target.GetProperty("paused").GetBoolean());
        Assert.True(target.GetProperty("pausedChange").GetBoolean());
    }

    [Fact]
    public void A_field_is_marked_after_the_delay_without_aria_busy_and_clear_once_it_is_answered()
    {
        var root = Run();

        var before = root.GetProperty("beforeDelay").GetBoolean();
        var during = root.GetProperty("duringLoading").GetBoolean();
        var busy = root.GetProperty("busyWhileLoading").GetBoolean();
        var after = root.GetProperty("afterEnd").GetBoolean();
        var buttonBusy = root.GetProperty("buttonBusy").GetBoolean();

        Assert.False(before);
        Assert.True(during);
        Assert.False(busy);
        Assert.False(after);
        Assert.True(buttonBusy);
    }

    [Fact]
    public void A_field_that_says_now_is_marked_the_moment_its_question_leaves_and_a_button_still_waits_the_delay()
    {
        var root = Run();

        var now = root.GetProperty("now");

        Assert.True(now.GetProperty("atOnce").GetBoolean());
        Assert.False(now.GetProperty("busy").GetBoolean());
        Assert.False(now.GetProperty("otherAtOnce").GetBoolean());
        Assert.True(now.GetProperty("still").GetBoolean());
        Assert.False(now.GetProperty("afterEnd").GetBoolean());
        Assert.False(now.GetProperty("quickLeft").GetBoolean());
    }
}
