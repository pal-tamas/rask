using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;
using static Rask.Server.Tests.WebSockets.HeardFieldTests;

#pragma warning disable RASK019 // small test pages; their <head> is not what is under test

namespace Rask.Server.Tests.WebSockets;

// A field that holds a secret is never given back its text: the page says only how many times it heard it
// (data-rask-heard="1"), and nothing the reader typed into it is in any frame the server sends.
public class SecretHeardTests
{
    private const string Typed = "hunter2-correct-horse";

    [Theory]
    [InlineData(LiveTransportKind.WebSocket, LiveDiffMode.DisabledFull)]
    [InlineData(LiveTransportKind.WebSocket, LiveDiffMode.Forced)]
    [InlineData(LiveTransportKind.Http, LiveDiffMode.Forced)]
    public async Task A_password_the_submit_empties_is_heard_as_a_count_and_its_text_is_in_no_frame(LiveTransportKind transport, LiveDiffMode mode)
    {
        using var host = RaskTestHost.Create<SecretBoxApp>(diffMode: mode);
        var (html, sessionId) = await StartAsync(host);
        await using var connection = await LiveTestConnection.OpenAsync(host, transport, sessionId);

        await connection.SendRawAsync(Batch(
            Change(html, "password", Typed), Change(html, "code", Typed), Change(html, "token", Typed),
            $$$"""{"id":"{{{Submit(html)}}}","type":"submit","form":{}}"""));
        var frames = await connection.SettledAsync();

        var all = string.Concat(frames);
        Assert.Contains("sent=done", all, StringComparison.Ordinal);
        // The word is there (named in the diff's ops or in the page), and says nothing of the text.
        Assert.Contains("data-rask-heard", all, StringComparison.Ordinal);
        Assert.DoesNotContain(":" + Typed, all, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LiveTransportKind.WebSocket, LiveDiffMode.DisabledFull)]
    [InlineData(LiveTransportKind.WebSocket, LiveDiffMode.Forced)]
    public async Task What_was_typed_into_a_password_is_in_no_frame_the_server_sends(LiveTransportKind transport, LiveDiffMode mode)
    {
        using var host = RaskTestHost.Create<SecretBoxApp>(diffMode: mode);
        var (html, sessionId) = await StartAsync(host);
        await using var connection = await LiveTestConnection.OpenAsync(host, transport, sessionId);

        await connection.SendRawAsync(Batch(
            Change(html, "password", Typed), $$$"""{"id":"{{{Submit(html)}}}","type":"submit","form":{}}"""));
        var frames = await connection.SettledAsync();

        Assert.DoesNotContain(Typed, string.Concat(frames), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_password_field_renders_the_count_alone_beside_it()
    {
        using var host = RaskTestHost.Create<SecretBoxApp>(diffMode: LiveDiffMode.DisabledFull);
        var (html, sessionId) = await StartAsync(host);
        await using var connection = await LiveTestConnection.OpenAsync(host, LiveTransportKind.WebSocket, sessionId);

        await connection.SendRawAsync(Batch(
            Change(html, "password", Typed), $$$"""{"id":"{{{Submit(html)}}}","type":"submit","form":{}}"""));
        var answer = await connection.ReceiveUntilAsync(frame => frame.Contains("sent=done", StringComparison.Ordinal), "the submit's render");

        Assert.Matches("<input[^>]*id=\"password\"[^>]*data-rask-heard=\"1\"[ />]", HtmlOf(answer));
        Assert.DoesNotContain(":" + Typed, answer, StringComparison.Ordinal);
    }
}

/// <summary>Three fields whose text must never come back: a password, one the browser fills as a code, one the app marks.</summary>
public sealed partial class SecretBoxApp : Component
{
    private readonly Secrets _secrets = new();
    private string _sent = "";

    protected override Component? HeadAssets => Markup.Title["secrets"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Markup.P[$"sent={_sent}"],
        Form.Model(_secrets).OnSubmit(Send).NoValidate(true)[
            Input.Bind(() => _secrets.Password).Type(InputType.Password).Id("password"),
            Input.Bind(() => _secrets.Code).Autocomplete("one-time-code").Id("code"),
            Input.Bind(() => _secrets.Token).Data("rask-secret", "").Id("token")
        ]
    ];

    private void Send(Secrets secrets)
    {
        _sent = secrets.Password.Length > 0 ? "done" : "short";
        secrets.Password = "";
        secrets.Code = "";
        secrets.Token = "";
    }

    private sealed class Secrets
    {
        public string Password { get; set; } = "";

        public string Code { get; set; } = "";

        public string Token { get; set; } = "";
    }
}
