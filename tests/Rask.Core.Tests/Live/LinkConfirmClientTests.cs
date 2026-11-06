using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     A link that asks first and runs no handler — <c>Ui.MenuItem.Href("/logout").Confirm("Sign out?")</c>: asked
///     before the browser or the host follows it, and followed by the press a yes makes again.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-clicks.ts</c> and <c>rask-confirm.ts</c> in-process on Jint
///     (<c>LinkConfirmFixture.ts</c>). The kit's dialog asking it on WebAssembly is <c>ConfirmWasmTests</c> in
///     <c>Rask.Site.E2E.Tests</c>.
/// </remarks>
public sealed class LinkConfirmClientTests
{
    private static JsonElement Run() => ScriptFixture.Run("LinkConfirmFixture");

    [Fact]
    public void A_link_that_asks_nothing_is_followed_without_a_dialog()
    {
        var root = Run();

        var now = root.GetProperty("plainNow").GetBoolean();
        var asked = root.GetProperty("askedOfPlain").GetInt32();

        Assert.True(now);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void A_link_said_no_to_goes_nowhere()
    {
        var root = Run();

        var now = root.GetProperty("refusedNow").GetBoolean();
        var later = root.GetProperty("refusedLater").GetInt32();
        var asked = root.GetProperty("askedWhenRefused").EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.False(now);
        Assert.Equal(0, later);
        Assert.Equal(["Sign out?"], asked);
    }

    [Fact]
    public void A_link_said_yes_to_is_followed_by_the_press_made_again()
    {
        var root = Run();

        var now = root.GetProperty("acceptedNow").GetBoolean();
        var later = root.GetProperty("acceptedLater").GetInt32();
        var asked = root.GetProperty("askedWhenAccepted").EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.False(now);
        Assert.Equal(1, later);
        Assert.Equal(["Sign out?"], asked);
    }

    [Fact]
    public void A_link_that_runs_a_handler_is_left_to_the_handler_click_to_ask()
    {
        var root = Run();

        var now = root.GetProperty("handledNow").GetBoolean();
        var asked = root.GetProperty("askedOfHandled").GetInt32();

        Assert.True(now);
        Assert.Equal(0, asked);
    }
}
