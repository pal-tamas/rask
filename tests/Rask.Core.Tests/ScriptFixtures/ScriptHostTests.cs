using System.Text.Json.Nodes;

namespace Rask.Core.Tests.ScriptFixtures;

/// <summary>
///     The host every script fixture runs on (<c>ScriptFixture.Run</c>, Jint in-process): its virtual clock, the web
///     globals it gives a bare engine, and the runs it refuses to report from. Driven through
///     <c>ScriptHostFixture.ts</c>.
/// </summary>
/// <remarks>
///     The expected values are what Node printed for the same fixture before the host replaced it, so a fixture
///     written against one reads the same on the other.
/// </remarks>
public sealed class ScriptHostTests
{
    [Fact]
    public void Timers_fire_by_due_time_then_set_order_after_the_microtasks_and_the_clock_jumps_to_them()
    {
        var said = ScriptFixture.Run("ScriptHostFixture", "clock");

        Assert.Equal(["microtask", "zero", "one", "sooner", "later"],
            said.GetProperty("order").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(3, said.GetProperty("ticks").GetInt32());
        Assert.Equal(300, said.GetProperty("dateMoved").GetDouble());
        Assert.Equal(300, said.GetProperty("performanceMoved").GetDouble());
    }

    [Fact]
    public void The_web_globals_answer_as_a_browser_does()
    {
        var said = ScriptFixture.Run("ScriptHostFixture", "globals");

        Assert.Equal(
            JsonNode.Parse("""{"bytes":[97,195,169,226,130,172,240,159,152,128],"split":"aé€😀","url":"https://example.test/a/b/c?x=1+2&y=a%26b#h","origin":"https://example.test","pathname":"/a/b/c","query":[["1","3"],"two words","a=1&b=two+words&a=3"],"headers":["text/plain","1, 2",["content-type","x-many"]],"heard":1,"aborted":"AbortError","base64":["aGkgdGhlcmU=","hi there"],"cloned":true,"response":"streamed body","json":{"ok":true}}""")!.ToJsonString(),
            JsonNode.Parse(said.GetRawText())!.ToJsonString());
    }

    [Theory]
    [InlineData("throws", "The fixture threw: Error: the fixture's own stub broke")]
    [InlineData("rejects", "Unhandled rejection: Error: nobody handled this")]
    [InlineData("twice", "report(...) was called twice")]
    [InlineData("silent", "ran to the end without calling report(...)")]
    [InlineData("endless", "still running after a virtual day")]
    public void A_run_that_cannot_vouch_for_its_result_fails_and_says_why(string scenario, string why)
    {
        var failure = Record.Exception(() => ScriptFixture.Run("ScriptHostFixture", scenario));

        Assert.NotNull(failure);
        Assert.Contains(why, failure.Message, StringComparison.Ordinal);
    }
}
