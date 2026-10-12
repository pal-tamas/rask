using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.Tests.WebSockets;

/// <summary>
///     An edit form over the socket: text that arrives and does not fit the bound member must not leave the
///     stored value standing as if it had been typed. The field says what it could not read and the save does
///     not run — whether the value travels with the submit in one batch or a frame ahead of it.
/// </summary>
/// <remarks>Asserts against the full-HTML payload, so each host is created with the diff codec off.</remarks>
public class UnreadValueSubmitTests
{
    [Theory]
    [InlineData("weight", "99999999999", "The number is out of range.")]
    [InlineData("weight", "6500.5", "Enter a whole number.")]
    [InlineData("share", "1e30", "The number is out of range.")]
    [InlineData("day", "2026-02-31", "Enter a valid date.")]
    public async Task A_value_that_does_not_fit_sent_in_one_batch_with_the_submit_refuses_the_save(
        string field, string typed, string sentence)
    {
        using var host = RaskTestHost.Create<EditTankApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host);
        await using var connection = ws;

        await ws.SendJsonAsync(new { type = "batch", events = new[] { Change(page, field, typed), Submit(page, seq: 1) } });
        var html = await ReadUntilAckAsync(ws);

        Assert.Contains(sentence, html, StringComparison.Ordinal);
        Assert.Contains("saved=;", html, StringComparison.Ordinal);
        Assert.Contains("stored=6500|27.5|2026-10-12;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_value_that_does_not_fit_sent_a_frame_ahead_of_the_submit_refuses_it_and_a_correction_saves()
    {
        using var host = RaskTestHost.Create<EditTankApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host);
        await using var connection = ws;

        await ws.SendJsonAsync(Change(page, "weight", "99999999999"));
        await ws.SendJsonAsync(Submit(page, seq: 1));
        var refused = await ReadUntilAckAsync(ws);
        await ws.SendJsonAsync(new { type = "batch", events = new[] { Change(refused, "weight", "7000"), Submit(refused, seq: 2) } });
        var saved = await ReadUntilAckAsync(ws);

        Assert.Contains("The number is out of range.", refused, StringComparison.Ordinal);
        Assert.Contains("saved=;", refused, StringComparison.Ordinal);
        Assert.Contains("saved=7000|27.5|2026-10-12;", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("The number is out of range.", saved, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("weight", "number", "Enter a whole number.")]
    [InlineData("share", "number", "Enter a number.")]
    [InlineData("day", "date", "Enter a valid date.")]
    public async Task Text_the_browser_held_back_arrives_as_an_empty_value_that_says_so_and_refuses_the_save(
        string field, string input, string sentence)
    {
        using var host = RaskTestHost.Create<EditTankApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host);
        await using var connection = ws;

        await ws.SendJsonAsync(new { type = "batch", events = new[] { HeldBack(page, field, input), Submit(page, seq: 1) } });
        var html = await ReadUntilAckAsync(ws);

        Assert.Contains(sentence, html, StringComparison.Ordinal);
        Assert.Contains("saved=;", html, StringComparison.Ordinal);
        Assert.Contains("stored=6500|27.5|2026-10-12;", html, StringComparison.Ordinal);
    }

    private static async Task<(string Page, ILiveTestConnection Ws)> OpenAsync(RaskTestHost host)
    {
        var page = await (await host.Http.GetAsync("/")).Content.ReadAsStringAsync();
        var ws = await LiveTestConnection.OpenAsync(host, LiveTransportKind.WebSocket, MarkupAssert.SessionId(page));
        return (page, ws);
    }

    private static object Change(string page, string field, string value) =>
        new { id = HandlerOf(page, field), type = "change", value };

    // What rask-input.ts sends for a number or date input holding text it reports as "".
    private static object HeldBack(string page, string field, string input) =>
        new { id = HandlerOf(page, field), type = "change", value = "", bad = input };

    private static string HandlerOf(string page, string field)
    {
        var id = Regex.Match(page, $"id=\"{field}\"[^>]*data-rask-on-change=\"([^\"]+)\"");
        Assert.True(id.Success, $"field '{field}' not found");
        return id.Groups[1].Value;
    }

    private static object Submit(string page, int seq) =>
        new { id = MarkupAssert.Attr(page, "data-rask-on-submit"), type = "submit", form = new { }, seq };

    // The page as the last render before the ack left it.
    private static async Task<string> ReadUntilAckAsync(ILiveTestConnection ws)
    {
        var html = string.Empty;
        while (true)
        {
            var text = await ws.TryReceiveTextAsync(LiveFrames.HangCeiling);
            Assert.NotNull(text);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("type", out var type) && type.ValueEquals("ack"))
            {
                return html;
            }

            if (doc.RootElement.TryGetProperty("html", out var rendered))
            {
                html = rendered.GetString()!;
            }
        }
    }
}

/// <summary>An edit form over a stored tank: every field already holds a value.</summary>
public sealed partial class EditTankApp : Component
{
    private readonly Tank _tank = new();
    private string _saved = "";

    protected override Component? HeadAssets => Markup.Title["edit tank"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        P[$"saved={_saved};stored={_tank};"],
        Form.Model(_tank).OnSubmit(tank => _saved = tank.ToString())[
            Input.Bind(() => _tank.Weight).Id("weight"),
            Validation.Message.Template(said => Span[said[0]]).For(() => _tank.Weight),
            Input.Bind(() => _tank.Share).Id("share"),
            Validation.Message.Template(said => Span[said[0]]).For(() => _tank.Share),
            Input.Bind(() => _tank.Day).Id("day"),
            Validation.Message.Template(said => Span[said[0]]).For(() => _tank.Day)
        ]
    ];

    private sealed class Tank
    {
        public int Weight { get; set; } = 6500;

        public decimal? Share { get; set; } = 27.5m;

        public DateOnly Day { get; set; } = new(2026, 10, 12);

        public override string ToString() => FormattableString.Invariant($"{Weight}|{Share}|{Day:yyyy-MM-dd}");
    }
}
