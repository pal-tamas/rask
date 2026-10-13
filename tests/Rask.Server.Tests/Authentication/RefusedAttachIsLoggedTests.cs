using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Rask.Core.Diagnostics;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Authentication;

/// <summary>
///     A refused attach is answered on the wire exactly like an id that never existed (#1075), so the log is the only
///     place an operator can tell the three apart.
/// </summary>
[Collection("DiagnosticsSink")]
public sealed class RefusedAttachIsLoggedTests
{
    private const string UserHeader = "X-Test-User";

    [Theory]
    [InlineData("alice", "mallory", "it belongs to another user")]
    [InlineData("alice", null, "it has a user and the connection is anonymous")]
    public async Task A_refused_attach_is_logged_with_its_reason(string owner, string? caller, string reason)
    {
        using var host = RaskTestHost.Create<NoOpApp>(configureMiddleware: StampUser);
        var sessionId = await OpenSessionAsync(host, owner);

        var logged = await HelloAsync(host, sessionId, caller);

        Assert.Contains(reason, Assert.Single(logged).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hello_for_an_id_this_host_never_had_is_logged_as_unknown()
    {
        using var host = RaskTestHost.Create<NoOpApp>(configureMiddleware: StampUser);

        var logged = await HelloAsync(host, "no-such-session", "alice");

        Assert.Contains("no session has that id", Assert.Single(logged).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_line_names_neither_the_session_id_nor_anyone()
    {
        using var host = RaskTestHost.Create<NoOpApp>(configureMiddleware: StampUser);
        var sessionId = await OpenSessionAsync(host, "alice");

        var line = Assert.Single(await HelloAsync(host, sessionId, "mallory"));

        Assert.Equal(RaskLogLevel.Information, line.Level);
        Assert.DoesNotContain(sessionId, line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("alice", line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("mallory", line.Message, StringComparison.Ordinal);
    }

    private static void StampUser(IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Headers.TryGetValue(UserHeader, out var name) && !string.IsNullOrEmpty(name))
            {
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, name!)], "Test"));
            }

            await next(ctx);
        });

    private static async Task<string> OpenSessionAsync(RaskTestHost host, string user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(UserHeader, user);
        using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
        return MarkupAssert.SessionId(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // Sends a hello as the caller and returns what the refusal logged.
    private static async Task<RaskDiagnosticEvent[]> HelloAsync(RaskTestHost host, string sessionId, string? caller)
    {
        var captured = new ConcurrentQueue<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Enqueue;
        try
        {
            host.WebSockets.ConfigureRequest = request =>
            {
                if (caller is not null)
                {
                    request.Headers[UserHeader] = caller;
                }
            };
            using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId }, TestContext.Current.CancellationToken);
            await ws.ReceiveUntilAsync(
                static frame => frame.Contains("\"status\":\"unknown\"", StringComparison.Ordinal), "the refusal");

            return [.. captured.Where(static e => e.Message.Contains("refused", StringComparison.Ordinal))];
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }
}
