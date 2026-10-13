using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.App;

/// <content>A credential that arrives while a handler runs: the handler finishes as who it started as.</content>
public sealed partial class HandWiredDataHostTests
{
    private static readonly Guid Seven = new("00000000-0000-0000-0000-000000000007");
    private static readonly Guid Eight = new("00000000-0000-0000-0000-000000000008");

    [Theory]
    [InlineData(TenantFrom.ThePrincipal)]
    [InlineData(TenantFrom.TheRaskClaim)]
    public async Task A_handler_writes_in_the_tenant_it_read_in_when_another_credential_arrives_between_the_two(TenantFrom tenantFrom)
    {
        using var host = await StartAsync(tenantFrom);
        using var page = await OpenAsync(host, tenantFrom);
        var gate = host.Services.GetRequiredService<HandlerGate>();

        using var second = await ArriveWhileAddingAsync(host, page, partner: 8);
        var whileItRan = TenantOf(page.Session);
        gate.Open.SetResult();
        await second.ReceiveUntilAsync(Shows(Eight), "the render for the adopted principal");

        Assert.Equal(Seven, gate.ReadIn);
        Assert.Equal(Seven, gate.WroteIn);
        Assert.Equal(Seven, whileItRan);
        Assert.Equal(7, await TenantOfRowAsync(host, "Added"));
    }

    [Fact]
    public async Task The_event_after_a_handler_another_credential_arrived_in_runs_as_the_new_principal()
    {
        using var host = await StartAsync(TenantFrom.ThePrincipal);
        using var page = await OpenAsync(host, TenantFrom.ThePrincipal);
        using var second = await ArriveWhileAddingAsync(host, page, partner: 8);

        host.Services.GetRequiredService<HandlerGate>().Open.SetResult();
        var after = await LoadAsync(second, page.Html, "Vienna");

        Assert.Contains($"tenant={Eight}", after, StringComparison.Ordinal);
        Assert.Null(page.Session.ArrivedUser);
    }

    // The first frame the new connection is sent is the handler's own render, as the user it ran for.
    [Fact]
    public async Task Nothing_is_drawn_for_a_principal_that_arrived_mid_handler_until_it_was_adopted()
    {
        using var host = await StartAsync(TenantFrom.ThePrincipal);
        using var page = await OpenAsync(host, TenantFrom.ThePrincipal);
        using var second = await ArriveWhileAddingAsync(host, page, partner: 8);

        host.Services.GetRequiredService<HandlerGate>().Open.SetResult();
        var firstFrame = await second.ReceiveTextAsync();
        var adopted = await second.ReceiveUntilAsync(Shows(Eight), "the render for the adopted principal");

        Assert.Contains($"tenant={Seven}", firstFrame, StringComparison.Ordinal);
        Assert.Contains("names=Budapest,Szeged<", firstFrame, StringComparison.Ordinal);
        Assert.Contains($"tenant={Eight}", adopted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Of_two_credentials_that_arrive_while_one_handler_runs_the_later_is_the_one_adopted()
    {
        using var host = await StartAsync(TenantFrom.ThePrincipal);
        using var page = await OpenAsync(host, TenantFrom.ThePrincipal);
        using var second = await ArriveWhileAddingAsync(host, page, partner: 9);
        var earlier = page.Session.ArrivedUser;

        using var third = await AttachAsync(host, page.Session.Id, partner: 8);
        await WaitFor.True(() => !ReferenceEquals(page.Session.ArrivedUser, earlier), "the later credential is held");
        host.Services.GetRequiredService<HandlerGate>().Open.SetResult();
        await third.ReceiveUntilAsync(Shows(Eight), "the render for the adopted principal");

        Assert.Equal(Eight, TenantOf(page.Session));
        Assert.Null(page.Session.ArrivedUser);
    }

    // The handler holds the session's lock and waits for a frame only this connection can bring.
    [Fact]
    public async Task An_attach_while_a_handler_waits_for_the_page_s_script_is_not_made_to_wait_for_the_handler()
    {
        using var host = await StartAsync(TenantFrom.ThePrincipal);
        using var page = await OpenAsync(host, TenantFrom.ThePrincipal);
        await page.Socket.SendJsonAsync(new { id = HandlerOf(page.Html, "ask") }, ct: TestContext.Current.CancellationToken);
        var asked = await page.Socket.ReceiveUntilAsync(static f => f.Contains("jsInvokes", StringComparison.Ordinal), "the call into the page");

        var carried = page.Session.ConnectionUser;
        using var second = await AttachAsync(host, page.Session.Id, partner: 8);
        await WaitFor.True(() => Arrived(page.Session, carried), "the credential arrives");
        await second.SendJsonAsync(
            new { type = "jsResult", id = InvokeIdIn(asked), success = true, result = "Asked" }, ct: TestContext.Current.CancellationToken);
        await second.ReceiveUntilAsync(Shows(Eight), "the render for the adopted principal");

        Assert.Equal(Seven, host.Services.GetRequiredService<HandlerGate>().WroteIn);
        Assert.Equal(7, await TenantOfRowAsync(host, "Asked"));
    }

    [Fact]
    public async Task A_credential_that_arrives_with_nothing_running_is_adopted_before_the_attach_returns()
    {
        using var host = await StartAsync(TenantFrom.ThePrincipal);
        using var page = await OpenAsync(host, TenantFrom.ThePrincipal);

        using var second = await AttachAsync(host, page.Session.Id, partner: 8);
        await second.ReceiveUntilAsync(Shows(Eight), "the render the attach owes");

        Assert.Equal(Eight, TenantOf(page.Session));
        Assert.Null(page.Session.ArrivedUser);
    }

    // Alice's page, opened and attached as partner 7.
    private static async Task<OpenPage> OpenAsync(RaskTestHost host, TenantFrom tenantFrom)
    {
        host.Services.GetRequiredService<ChosenPartner>().Kind = tenantFrom;
        var opened = await GetAsync(host, "/start", tenant: null, user: Alice, partner: 7);
        var sessionId = MarkupAssert.SessionId(opened);
        var socket = await AttachAsync(host, sessionId, partner: 7);
        return new OpenPage(host.Store.Peek(sessionId)!, socket, opened);
    }

    private sealed record OpenPage(LiveSession Session, WebSocket Socket, string Html) : IDisposable
    {
        public void Dispose() => Socket.Dispose();
    }

    // Presses "add", and attaches a connection for another partner once the handler has read and is waiting.
    private static async Task<WebSocket> ArriveWhileAddingAsync(RaskTestHost host, OpenPage page, int partner)
    {
        var session = page.Session;
        await page.Socket.SendJsonAsync(new { id = HandlerOf(page.Html, "add") }, ct: TestContext.Current.CancellationToken);
        await host.Services.GetRequiredService<HandlerGate>().Reached.Task.WaitAsync(LiveFrames.HangCeiling, TestContext.Current.CancellationToken);

        var carried = session.ConnectionUser;
        var second = await AttachAsync(host, session.Id, partner);
        await WaitFor.True(() => Arrived(session, carried), "the credential arrives");
        return second;
    }

    // The session has heard of the new connection's credential: it holds it for later, or has adopted it.
    private static bool Arrived(LiveSession session, System.Security.Claims.ClaimsPrincipal? carried) =>
        session.ArrivedUser is not null || !ReferenceEquals(session.ConnectionUser, carried);

    private static Func<string, bool> Shows(Guid tenant) => frame => frame.Contains($"tenant={tenant}", StringComparison.Ordinal);

    private static string HandlerOf(string html, string button)
    {
        var match = Regex.Match(html, "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>" + Regex.Escape(button) + "<");
        Assert.True(match.Success, $"button '{button}' not found");
        return match.Groups[1].Value;
    }

    private static long InvokeIdIn(string frame)
    {
        using var json = JsonDocument.Parse(frame);
        return json.RootElement.GetProperty("jsInvokes")[0].GetProperty("id").GetInt64();
    }

    // The tenant column of the row the handler wrote, read by the context that owns the table.
    private static async Task<int?> TenantOfRowAsync(RaskTestHost host, string name)
    {
        await using var legacy = await host.Services.GetRequiredService<IDbContextFactory<LegacyTerminalContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        return (await legacy.Terminals.SingleAsync(t => t.Name == name, TestContext.Current.CancellationToken)).TenantId;
    }
}
