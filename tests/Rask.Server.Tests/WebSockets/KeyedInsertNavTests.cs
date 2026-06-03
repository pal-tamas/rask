using System.Net.WebSockets;
using System.Text.Json;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// Regression: a keyed InsertSubtree fragment is sliced from the rendered HTML using frame
// byte-offsets. RenderAsLiveRootCore splices the head-asset sentinel out of that HTML AFTER
// the offsets were captured, shifting every byte position after the head — so without the
// offset adjustment the inserted row's HTML was garbled (sliced mid-attribute). Keyed insert
// is the only op that carries an HTML slice, which is why delete/move were unaffected.
[Collection("SessionGracePeriod")]
public class KeyedInsertNavTests
{
    public KeyedInsertNavTests() => LiveOptions.DiffMode = LiveDiffMode.Forced;

    [Fact]
    public async Task KeyedInsertDuringNavigation_ShipsCorrectRowHtml()
    {
        await using var fixture = await ConnectedSession.Connect<KeyedNavApp>();

        // Seed the diff baseline (first interaction ships full HTML).
        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/list", query = "" });
        _ = await DrainAll(fixture.Ws);

        // Navigate to /add: the RouteState.Changed handler appends keyed item 3 — the keyed
        // InsertSubtree rides this navigation diff.
        await fixture.Ws.SendJsonAsync(new { type = "navigate", path = "/add", query = "" });
        var frames = await DrainAll(fixture.Ws);

        var insert = FindInsertSubtree(frames);
        Assert.NotNull(insert);
        // The fragment must be the complete, correctly-sliced <li> — not garbled bytes.
        Assert.Equal("<li class=\"row\" data-rask-key=\"3\">item 3</li>", insert);
    }

    // Walk every shipped diff frame; return the HTML payload of the first InsertSubtree op
    // (EditOpKind value 4 → [kind, path, html, domCount]).
    private static string? FindInsertSubtree(List<string> frames)
    {
        foreach (var frame in frames)
        {
            using var doc = JsonDocument.Parse(frame);
            if (!doc.RootElement.TryGetProperty("ops", out var ops))
            {
                continue;
            }

            foreach (var op in ops.EnumerateArray())
            {
                if (op.GetArrayLength() >= 3 && op[0].GetInt32() == 4)
                {
                    return op[2].GetString();
                }
            }
        }

        return null;
    }

    private static async Task<List<string>> DrainAll(WebSocket ws)
    {
        var all = new List<string>();
        while (await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(500)) is { } frame)
        {
            all.Add(frame);
        }

        return all;
    }
}
