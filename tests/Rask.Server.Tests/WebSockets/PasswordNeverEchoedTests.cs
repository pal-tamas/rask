using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.Tests.WebSockets;

/// <summary>
///     A password field's value goes browser to server and never back: the server writes it nowhere the browser
///     reads, except as a clear, which carries no secret.
/// </summary>
public class PasswordNeverEchoedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Typed = "typed-by-the-user";

    public static TheoryData<LiveTransportKind> Transports() => LiveTestConnection.Transports();

    [Fact]
    public async Task A_password_held_by_the_model_is_not_written_into_the_first_page()
    {
        using var host = RaskTestHost.Create<PrefilledPasswordApp>();

        var page = await (await host.Http.GetAsync("/", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("type=\"password\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain(PrefilledPasswordApp.Secret, page, StringComparison.Ordinal);
        Assert.Contains("len=" + PrefilledPasswordApp.Secret.Length, page, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task A_password_the_server_changes_after_it_was_typed_is_sent_to_no_frame(LiveTransportKind kind)
    {
        using var host = RaskTestHost.Create<PasswordApp>();
        var (page, ws) = await OpenAsync(host, kind);
        await using var connection = ws;

        await ws.SendJsonAsync(new { id = ChangeOf(page), type = "change", value = Typed });
        var typedFrames = await ws.SettledAsync();
        await ws.SendJsonAsync(new { id = ClickOf(page, "set"), type = "click" });
        var setFrames = await ws.SettledAsync();

        Assert.Contains(typedFrames, f => f.Contains("len=" + Typed.Length, StringComparison.Ordinal));
        Assert.Contains(setFrames, f => f.Contains("len=" + PasswordApp.Replacement.Length, StringComparison.Ordinal));
        Assert.All(typedFrames.Concat(setFrames), f =>
        {
            Assert.DoesNotContain(Typed, f, StringComparison.Ordinal);
            Assert.DoesNotContain(PasswordApp.Replacement, f, StringComparison.Ordinal);
        });
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task A_password_the_server_clears_is_sent_as_an_empty_value(LiveTransportKind kind)
    {
        using var host = RaskTestHost.Create<PasswordApp>();
        var (page, ws) = await OpenAsync(host, kind);
        await using var connection = ws;

        await ws.SendJsonAsync(new { id = ChangeOf(page), type = "change", value = Typed });
        await ws.SettledAsync();
        await ws.SendJsonAsync(new { id = ClickOf(page, "clear"), type = "click" });
        var frames = await ws.SettledAsync();

        Assert.Contains(frames, f => f.Contains("\"value\",\"\"", StringComparison.Ordinal));
        Assert.All(frames, f => Assert.DoesNotContain(Typed, f, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_password_the_server_changes_is_not_in_a_whole_page_reply()
    {
        using var host = RaskTestHost.Create<PasswordApp>(diffMode: LiveDiffMode.DisabledFull);
        var (page, ws) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var connection = ws;

        await ws.SendJsonAsync(new { id = ChangeOf(page), type = "change", value = Typed });
        var typedFrames = await ws.SettledAsync();
        await ws.SendJsonAsync(new { id = ClickOf(page, "set"), type = "click" });
        var setFrames = await ws.SettledAsync();

        Assert.Contains(setFrames, f => f.Contains("\"html\"", StringComparison.Ordinal));
        Assert.All(typedFrames.Concat(setFrames), f =>
        {
            Assert.DoesNotContain(Typed, f, StringComparison.Ordinal);
            Assert.DoesNotContain(PasswordApp.Replacement, f, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_field_that_is_not_a_secret_still_echoes_a_value_the_server_sets()
    {
        using var host = RaskTestHost.Create<PasswordApp>();
        var (page, ws) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var connection = ws;

        await ws.SendJsonAsync(new { id = ClickOf(page, "name"), type = "click" });
        var frames = await ws.SettledAsync();

        Assert.Contains(frames, f => f.Contains(PasswordApp.Replacement, StringComparison.Ordinal));
    }

    private static async Task<(string Page, ILiveTestConnection Ws)> OpenAsync(RaskTestHost host, LiveTransportKind kind)
    {
        var page = await (await host.Http.GetAsync("/", Ct)).Content.ReadAsStringAsync(Ct);
        var ws = await LiveTestConnection.OpenAsync(host, kind, MarkupAssert.SessionId(page));
        return (page, ws);
    }

    private static string ChangeOf(string page)
    {
        var id = Regex.Match(page, "id=\"pw\"[^>]*data-rask-on-change=\"([^\"]+)\"");
        Assert.True(id.Success, page);
        return id.Groups[1].Value;
    }

    private static string ClickOf(string page, string button)
    {
        var id = Regex.Match(page, $"id=\"{button}\"[^>]*data-rask-on-click=\"([^\"]+)\"");
        Assert.True(id.Success, page);
        return id.Groups[1].Value;
    }
}

/// <summary>A sign-in field the server can also write: to a new value, to nothing, and to a plain field beside it.</summary>
public sealed partial class PasswordApp : Component
{
    public const string Replacement = "server-chosen-value";

    private readonly Model _m = new();

    protected override Component? HeadAssets => Markup.Title["password"];

    protected override Component? Render() =>
    [
        Form.Model(_m)[
            Input.Bind(() => _m.Password).Type(InputType.Password).Id("pw"),
            Input.Bind(() => _m.Name).Id("nm")],
        Button.Id("set").OnClick(() => _m.Password = Replacement)["set"],
        Button.Id("clear").OnClick(() => _m.Password = "")["clear"],
        Button.Id("name").OnClick(() => _m.Name = Replacement)["name"],
        Markup.P[$"len={_m.Password.Length}"]
    ];

    private sealed class Model
    {
        public string Password { get; set; } = "";

        public string Name { get; set; } = "";
    }
}

/// <summary>A page that, wrongly, starts with a password in its model.</summary>
public sealed partial class PrefilledPasswordApp : Component
{
    public const string Secret = "preset-secret-9";

    private readonly Model _m = new() { Password = Secret };

    protected override Component? HeadAssets => Markup.Title["prefilled"];

    protected override Component? Render() =>
    [
        Form.Model(_m)[Input.Bind(() => _m.Password).Type(InputType.Password).Id("pw")],
        Markup.P[$"len={_m.Password.Length}"]
    ];

    private sealed class Model
    {
        public string Password { get; set; } = "";
    }
}
