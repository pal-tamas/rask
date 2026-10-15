using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     The mark a field carries while the event that sent its value is unanswered: <c>data-loading</c>, as Livewire
///     puts it on any element that triggered a request, and no <c>aria-busy</c>.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-loading.ts</c> in a Node subprocess with a fake clock (<c>FieldLoadingFixture.ts</c>).
///     The hosts' release — the ack, the dispatch promise — is <c>FieldBusyMarkTests</c> in
///     <c>Rask.Server.E2E.Tests</c> and <c>FieldBusyMarkWasmTests</c> in <c>Rask.Site.E2E.Tests</c>.
/// </remarks>
public sealed class FieldLoadingTests
{
    private static JsonElement? Run() => NodeFixture.Run("FieldLoadingFixture");

    [Fact]
    public void An_input_or_change_that_was_sent_names_its_field_and_nothing_else_does()
    {
        if (Run() is not { } root)
        {
            return;
        }

        var target = root.GetProperty("target");

        Assert.True(target.GetProperty("input").GetBoolean());
        Assert.True(target.GetProperty("change").GetBoolean());
        Assert.True(target.GetProperty("click").GetBoolean());
        Assert.True(target.GetProperty("unasked").GetBoolean());
        Assert.True(target.GetProperty("gone").GetBoolean());
        Assert.True(target.GetProperty("optedOut").GetBoolean());
    }

    [Fact]
    public void A_field_is_marked_after_the_delay_without_aria_busy_and_clear_once_it_is_answered()
    {
        if (Run() is not { } root)
        {
            return;
        }

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
}
