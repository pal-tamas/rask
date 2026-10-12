using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// A callout written ahead of two stateful components used to rebuild both every time it came or went. On this
// host that is a list that loads again and a count that goes back to nought, in the browser's own session.
[Collection("WasmSession")]
public class ConditionalSiblingStateTests() : ResettingTestBase(LiveDiffMode.Forced)
{
    [Fact]
    public async Task A_callout_that_appears_above_a_list_and_a_count_leaves_what_they_hold()
    {
        var loads = new ListLoads();
        var session = NewSession<CalloutAboveApp>(configure: s => s.AddSingleton(loads), diffMode: DiffMode).session;
        var html = Html(await session.InitialRenderAsync());
        var pressed = await Click(session, html, "press");

        var shown = await Click(session, html, "save");
        var hidden = await Click(session, html, "save");
        var pressedAgain = await Click(session, html, "press");

        Assert.Contains("load 1", html);
        Assert.Contains("pressed 1", pressed);
        Assert.Contains("Saved", shown);
        Assert.DoesNotContain("pressed 0", shown);
        Assert.DoesNotContain("pressed 0", hidden);
        Assert.Contains("pressed 2", pressedAgain);
        Assert.Equal(1, loads.Count);
    }

    private static string Html(byte[] frame)
    {
        using var doc = JsonDocument.Parse(frame.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }

    private static async Task<string> Click(WasmLiveSession session, string html, string buttonId)
    {
        var handler = Regex.Match(html, $"id=\"{buttonId}\" data-rask-on-click=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEqual(string.Empty, handler);
        return Encoding.UTF8.GetString(
            await session.DispatchAsync(Encoding.UTF8.GetBytes($$"""{"id":"{{handler}}","type":"click"}""")));
    }
}
