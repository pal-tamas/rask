using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

[Collection("WasmSession")]
// Reads the `html` payload field, so the session renders whole (see DispatchAsyncRoutingTests).
public class DeferredFieldDispatchTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    private static string Html(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }

    private static string[] Handlers(string html, string name) =>
        [.. Regex.Matches(html, "data-rask-on-" + name + "=\"([^\"]+)\"").Select(match => match.Groups[1].Value)];

    [Fact]
    public async Task A_bound_field_and_a_bound_checkbox_render_as_controls_that_wait_for_the_next_action()
    {
        var (session, _) = NewSession<DeferredFieldStubApp>(diffMode: DiffMode);

        var initial = Html(await session.InitialRenderAsync());

        Assert.Equal(2, Regex.Count(initial, "data-rask-bind-on=\"action\""));
        Assert.Equal(2, Handlers(initial, "change").Length);
        Assert.Empty(Handlers(initial, "input"));
    }

    [Fact]
    public async Task The_changes_sent_ahead_of_a_submit_are_what_the_submit_saves()
    {
        var (session, _) = NewSession<DeferredFieldStubApp>(diffMode: DiffMode);
        var initial = Html(await session.InitialRenderAsync());

        var named = Html(await session.DispatchAsync(
            Utf8($$"""{"id":"{{Handlers(initial, "change")[0]}}","type":"change","value":"Atlantis"}""")));
        var agreed = Html(await session.DispatchAsync(
            Utf8($$"""{"id":"{{Handlers(named, "change")[1]}}","type":"change","value":"true"}""")));
        var saved = Html(await session.DispatchAsync(
            Utf8($$$"""{"id":"{{{Handlers(agreed, "submit")[0]}}}","type":"submit","form":{"Name":"Atlantis","Agreed":"on"}}""")));

        Assert.Contains("name=Atlantis|saved=Atlantis/True", saved, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_value_refused_when_it_arrives_shows_its_message_until_the_first_edit_is_reported()
    {
        var (session, _) = NewSession<DeferredFieldStubApp>(diffMode: DiffMode);
        var initial = Html(await session.InitialRenderAsync());
        var refused = Html(await session.DispatchAsync(
            Utf8($$"""{"id":"{{Handlers(initial, "change")[0]}}","type":"change","value":"At"}""")));

        var edited = Html(await session.DispatchAsync(
            Utf8($$"""{"id":"{{Handlers(refused, "edit")[0]}}","type":"edit"}""")));

        Assert.Contains("Name is too short.", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("Name is too short.", edited, StringComparison.Ordinal);
        Assert.Empty(Handlers(edited, "edit"));
        Assert.Contains("name=At|", edited, StringComparison.Ordinal);
    }
}
