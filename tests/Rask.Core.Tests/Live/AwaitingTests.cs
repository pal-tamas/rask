using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     What the page is waiting on (<c>rask-owned.ts</c>, <c>awaiting</c>): the hooks are told once, when the last
///     event or navigation a host sent has been answered, and an answer reported twice counts once.
/// </summary>
/// <remarks>
///     Drives the production module in a Node subprocess (<c>AwaitingFixture.ts</c>). The hosts' wiring — which send
///     takes one, which render answers it — and the table skeleton that waits on it are covered in a real browser by
///     <c>TableSkeletonTests</c>.
/// </remarks>
public sealed class AwaitingTests
{
    private static JsonElement? Run() => NodeFixture.Run("AwaitingFixture");

    [Fact]
    public void The_hooks_hear_once_when_the_last_answer_is_in_and_a_second_report_changes_nothing()
    {
        // Node is not installed: TableSkeletonTests covers the same in a browser.
        if (Run() is not { } said)
        {
            return;
        }

        Assert.False(said.GetProperty("idle").GetBoolean());
        Assert.True(said.GetProperty("whileTwo").GetBoolean());
        Assert.True(said.GetProperty("afterFirst").GetProperty("awaiting").GetBoolean());
        Assert.Equal(0, said.GetProperty("afterFirst").GetProperty("quiet").GetInt32());
        Assert.Equal(0, said.GetProperty("firstTwice").GetProperty("quiet").GetInt32());
        Assert.False(said.GetProperty("afterBoth").GetProperty("awaiting").GetBoolean());
        Assert.Equal(1, said.GetProperty("afterBoth").GetProperty("quiet").GetInt32());
        Assert.Equal(1, said.GetProperty("secondTwice").GetInt32());
    }
}
