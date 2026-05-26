using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Core.ScopedCss;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// Drives WasmLiveSession with LiveDiffMode.Auto so the dispatch path produces
// diff payloads. The Playwright suite (53 failures under Auto) suggested DOM-
// state divergence; this test reproduces the dispatch flow in-process so the
// shipped payload can be inspected directly. If the diff payload is sound
// here, the bug is on the client side; if not, the bug is server-side.
[Collection("WasmSession")]
public class WasmDiffPathTests
{
    public WasmDiffPathTests()
    {
        ScopedCssRegistry.InvalidateAll();
        // Forced (not Auto) so the diff payload is always shipped — the tiny
        // StubApp tree (~150 bytes HTML) is below the 25%-of-HTML choose-smaller
        // threshold and would route to full HTML under Auto.
        LiveOptions.DiffMode = LiveDiffMode.Forced;
    }

    [Fact]
    public async Task ClickCounter_ThreeIncrements_ProducesDiffsWithCorrectUpdateText()
    {
        var (session, _) = NewSession();
        var initial = await session.InitialRenderAsync();

        // Initial render is full HTML (first render of the session, no prior to diff).
        using (var initialDoc = JsonDocument.Parse(initial.AsMemory()))
        {
            Assert.True(initialDoc.RootElement.TryGetProperty("html", out _),
                "Initial render must be full HTML (kind != \"diff\"); got: "
                + Encoding.UTF8.GetString(initial));
        }

        var handlerId = ExtractFirstHandlerId(initial);
        Assert.NotNull(handlerId);
        // Surface the actual handlerId in the failure if subsequent dispatches return
        // empty — helps distinguish "unknown handler id" from "byte-dedup'd payload".
        Assert.False(string.IsNullOrEmpty(handlerId),
            $"Expected a non-empty handler id from initial render. Initial: {Encoding.UTF8.GetString(initial)[..Math.Min(500, initial.Length)]}");

        for (var click = 1; click <= 3; click++)
        {
            var result = await session.DispatchAsync(
                Encoding.UTF8.GetBytes($$"""{"id":"{{handlerId}}","type":"click"}"""));

            Assert.True(result.Length > 0,
                $"Click {click}: DispatchAsync returned empty bytes. handlerId={handlerId}. "
                + "Either handler missing (unknown id) or payload bytes equalled _lastAppliedPayload "
                + "(false-positive byte dedup against initial render).");
            var resultText = Encoding.UTF8.GetString(result);
            // Surface what we actually got — easier to diagnose than a raw KeyNotFoundException.
            Assert.True(resultText.StartsWith("{\"kind\":\"diff\""),
                $"Click {click}: expected diff payload (Forced mode) but got: {resultText[..Math.Min(300, resultText.Length)]}");
            using var doc = JsonDocument.Parse(result.AsMemory());
            var kind = doc.RootElement.GetProperty("kind").GetString();
            Assert.Equal("diff", kind);

            // The bumped count value appears as the UpdateText value slot on some op.
            // With the positional per-op format each op is `[kind, path, value]` for
            // UpdateText — so the value lives at op[2], not in a named field.
            var expected = $"count={click}";
            var ops = doc.RootElement.GetProperty("ops").EnumerateArray().ToList();
            var matching = ops.Where(o =>
                o.ValueKind == JsonValueKind.Array
                && o.GetArrayLength() >= 3
                && o[0].GetInt32() == (int)EditOpKind.UpdateText
                && o[2].ValueKind == JsonValueKind.String
                && o[2].GetString() == expected).ToList();
            Assert.True(matching.Count == 1,
                $"Click {click}: expected exactly one UpdateText op with value \"{expected}\". "
                + $"Got {ops.Count} ops: {doc.RootElement.GetRawText()}");
            // The path's first index addresses document.childNodes. For
            // Fragment[Doctype, Html[...]] there are only 2 top-level frames, so
            // path[0] must be 0 (doctype — but text nodes never live here) or 1
            // (html). The e2e diff log surfaced [6, ...] for the real App, which
            // can't be reached through legitimate frame walking.
            var path = matching[0][1].EnumerateArray().Select(e => e.GetInt32()).ToArray();
            Assert.True(path[0] is 0 or 1,
                $"Click {click}: expected path[0]∈{{0,1}}, got [{string.Join(",", path)}]");
        }
    }

    private static (WasmLiveSession session, IServiceProvider services) NewSession()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RouteState>();
        services.AddSingleton<Navigator>();
        var provider = services.BuildServiceProvider();
        var app = ActivatorUtilities.CreateInstance<StubApp>(provider);
        var session = new WasmLiveSession(app, provider);
        JSInterop.Init(session);
        return (session, provider);
    }

    private static string? ExtractFirstHandlerId(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString();
        if (html is null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(html, "data-rask-on-click=\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }
}
