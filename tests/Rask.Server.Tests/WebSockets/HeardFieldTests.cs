using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // small test pages; their <head> is not what is under test

namespace Rask.Server.Tests.WebSockets;

// #1245: a field that waits for the next action and is emptied by the submit it went with. The last render had it
// empty too, so the diff alone says nothing and the browser kept what was typed. The render that answers the
// submit says what it heard beside the field (data-rask-heard), which is how the browser tells it apart.
public class HeardFieldTests
{
    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task The_render_that_answers_a_submit_says_what_it_heard_beside_each_field_it_emptied(LiveTransportKind transport)
    {
        using var host = RaskTestHost.Create<ChatBoxApp>(diffMode: LiveDiffMode.DisabledFull);
        var (html, sessionId) = await StartAsync(host);
        await using var connection = await LiveTestConnection.OpenAsync(host, transport, sessionId);

        await connection.SendRawAsync(Batch(
            Change(html, "message", "hello"), Change(html, "notes", "two lines"), $$$"""{"id":"{{{Submit(html)}}}","type":"submit","form":{}}"""));
        var answer = HtmlOf(await connection.ReceiveUntilAsync(frame => frame.Contains("sent=hello|two lines", StringComparison.Ordinal), "the submit's render"));

        Assert.Matches("<input[^>]*id=\"message\"[^>]*value=\"\"[^>]*data-rask-heard=\"1:hello\"|<input[^>]*id=\"message\"[^>]*data-rask-heard=\"1:hello\"[^>]*value=\"\"", answer);
        Assert.Matches("<textarea[^>]*id=\"notes\"[^>]*data-rask-heard=\"1:two lines\"[^>]*></textarea>", answer);
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task The_same_message_sent_again_is_a_new_word_and_the_next_event_does_not_take_it_back(LiveTransportKind transport)
    {
        using var host = RaskTestHost.Create<ChatBoxApp>(diffMode: LiveDiffMode.DisabledFull);
        var (html, sessionId) = await StartAsync(host);
        await using var connection = await LiveTestConnection.OpenAsync(host, transport, sessionId);
        var submit = $$$"""{"id":"{{{Submit(html)}}}","type":"submit","form":{}}""";
        await connection.SendRawAsync(Batch(Change(html, "message", "hello"), submit));
        var first = HtmlOf(await connection.ReceiveUntilAsync(frame => frame.Contains("sent=hello|", StringComparison.Ordinal), "the first submit's render"));

        await connection.SendJsonAsync(new { id = Attr(first, "tick", "data-rask-on-click"), type = "click" });
        var between = HtmlOf(await connection.ReceiveUntilAsync(frame => frame.Contains("ticks=1", StringComparison.Ordinal), "the tick's render"));
        await connection.SendRawAsync(Batch(Change(first, "message", "hello"), submit));
        var again = HtmlOf(await connection.ReceiveUntilAsync(frame => frame.Contains("2:hello", StringComparison.Ordinal), "the second submit's render"));

        Assert.Contains("data-rask-heard=\"1:hello\"", first, StringComparison.Ordinal);
        Assert.Contains("data-rask-heard=\"1:hello\"", between, StringComparison.Ordinal);
        Assert.Contains("data-rask-heard=\"2:hello\"", again, StringComparison.Ordinal);
    }

    internal static async Task<(string Html, string SessionId)> StartAsync(RaskTestHost host)
    {
        var html = await (await host.Http.GetAsync("/start", TestContext.Current.CancellationToken)).Content
            .ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (html, MarkupAssert.SessionId(html));
    }

    internal static string Batch(params string[] events) => $$"""{"type":"batch","events":[{{string.Join(',', events)}}]}""";

    internal static string Change(string html, string id, string value) =>
        $$"""{"id":"{{Attr(html, id, "data-rask-on-change")}}","type":"change","value":"{{value}}"}""";

    internal static string Submit(string html) => Regex.Match(html, "data-rask-on-submit=\"(h\\d+)\"").Groups[1].Value;

    // The value of `attribute` on the element whose id is `id`.
    internal static string Attr(string html, string id, string attribute)
    {
        var tag = Regex.Match(html, $"<[a-z]+[^>]*id=\"{id}\"[^>]*>").Value;
        var match = Regex.Match(tag, $"{attribute}=\"([^\"]+)\"");
        Assert.True(match.Success, $"{attribute} on #{id} not found in {tag}");
        return match.Groups[1].Value;
    }

    internal static string HtmlOf(string frame)
    {
        using var doc = JsonDocument.Parse(frame);
        return doc.RootElement.GetProperty("html").GetString()!;
    }
}

/// <summary>A chat box: what is typed goes with the submit, which empties it.</summary>
public sealed partial class ChatBoxApp : Component
{
    private readonly Draft _draft = new();
    private string _sent = "";
    private string _tab = "";
    private int _ticks;

    protected override Component? HeadAssets => Markup.Title["chat"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Markup.P[$"sent={_sent};ticks={_ticks};tab={_tab}"],
        Button.Id("tick").OnClick(() => _ticks++)["tick"],
        Button.Id("look").OnClick(Look)["look"],
        Form.Model(_draft).OnSubmit(Send).NoValidate(true)[
            Input.Bind(() => _draft.Message).Id("message"),
            Textarea.Bind(() => _draft.Notes).Id("notes")
        ]
    ];

    private void Look()
    {
        var visibility = AmbientServices.Current!.GetRequiredService<PageVisibility>();
        _tab = visibility.Shown(CancellationToken.None).IsCompleted ? "shown" : "hidden";
    }

    private void Send(Draft draft)
    {
        _sent = $"{draft.Message}|{draft.Notes}";
        draft.Message = "";
        draft.Notes = "";
    }

    private sealed class Draft
    {
        public string Message { get; set; } = "";

        public string Notes { get; set; } = "";
    }
}
