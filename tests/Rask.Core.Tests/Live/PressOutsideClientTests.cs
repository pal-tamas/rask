using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     When a press outside a modal dismisses it: the first press of a sequence on its backdrop does, and the second
///     press of a double one does not.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-overlay.ts</c> in-process on Jint (<c>PressOutsideFixture.ts</c>). The journey
///     it was found in — a double press on an editor dialog's cross, whose second press answered the leave guard's
///     question the first one raised — is <c>EditorDialogClosesTests</c> in <c>Rask.Server.E2E.Tests</c>.
/// </remarks>
public sealed class PressOutsideClientTests
{
    private static JsonElement Run() => ScriptFixture.Run("PressOutsideFixture");

    [Fact]
    public void A_single_press_on_the_backdrop_cancels_and_closes_the_modal()
    {
        var root = Run();

        var cancels = root.GetProperty("singlePressCancels").GetInt32();
        var closed = root.GetProperty("singlePressCloses").GetBoolean();

        Assert.Equal(1, cancels);
        Assert.True(closed);
    }

    [Fact]
    public void The_second_press_of_a_double_press_on_the_backdrop_leaves_the_modal_open()
    {
        var root = Run();

        var cancels = root.GetProperty("secondPressCancels").GetInt32();
        var open = root.GetProperty("secondPressLeavesItOpen").GetBoolean();

        Assert.Equal(0, cancels);
        Assert.True(open);
    }

    [Fact]
    public void A_press_inside_the_box_dismisses_nothing()
    {
        var root = Run();

        var cancels = root.GetProperty("insideCancels").GetInt32();
        var open = root.GetProperty("insideLeavesItOpen").GetBoolean();

        Assert.Equal(0, cancels);
        Assert.True(open);
    }
}
