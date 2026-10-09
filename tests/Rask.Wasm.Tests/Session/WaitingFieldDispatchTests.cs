using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

[Collection("WasmSession")]
// Reads the `html` payload field, so the session renders whole (see DispatchAsyncRoutingTests).
public class WaitingFieldDispatchTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    private static string Html(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }

    private static string Handler(string html, string name) =>
        Regex.Match(html, "data-rask-on-" + name + "=\"([^\"]+)\"").Groups[1].Value;

    [Fact]
    public async Task The_one_message_a_pause_sends_binds_validates_and_asks_to_hear_the_next_edit()
    {
        var (session, _) = NewSession<WaitingFieldStubApp>(diffMode: DiffMode);
        var initial = Html(await session.InitialRenderAsync());

        var paused = Html(await session.DispatchAsync(
            Utf8($$"""{"id":"{{Handler(initial, "input")}}","type":"input","value":"At"}""")));

        Assert.Contains("data-rask-debounce=\"300\"", initial, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on-edit", initial, StringComparison.Ordinal);
        Assert.Contains("name=At", paused, StringComparison.Ordinal);
        Assert.Contains("Name is too short.", paused, StringComparison.Ordinal);
        Assert.Contains("data-rask-on-edit", paused, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_edit_a_waiting_field_reports_takes_its_message_away_and_leaves_the_model()
    {
        var (session, _) = NewSession<WaitingFieldStubApp>(diffMode: DiffMode);
        var initial = Html(await session.InitialRenderAsync());
        var paused = Html(await session.DispatchAsync(
            Utf8($$"""{"id":"{{Handler(initial, "input")}}","type":"input","value":"At"}""")));

        var edited = Html(await session.DispatchAsync(
            Utf8($$"""{"id":"{{Handler(paused, "edit")}}","type":"edit"}""")));

        Assert.DoesNotContain("Name is too short.", edited, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on-edit", edited, StringComparison.Ordinal);
        Assert.Contains("name=At", edited, StringComparison.Ordinal);
    }
}
