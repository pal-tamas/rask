using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core.Authentication;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Authentication;

/// <content>
///     The edges of "every event asks": a script's call into the server, the moment a connection attaches, a batch
///     that runs long, an answer that arrives late, and a token that was only issued again.
/// </content>
public sealed partial class SessionStaysItsUsersTests
{
    [Fact]
    public async Task A_script_s_call_into_the_server_is_not_made_for_a_user_whose_sign_in_ended()
    {
        using var host = CreateHost<FollowApp>(revalidate: true);
        var page = await OpenAsync(host, "/open", "alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");
        var mine = Guid.NewGuid().ToString("N");
        await InvokeAsync(ws, mine);
        await WaitFor.True(() => ScriptCalls.Count(mine) == 1, "the first call is made");

        host.Services.GetRequiredService<Directory>().SignedOut.Add("alice");
        host.Store.Peek(page.SessionId)!.LastUserCheck = 0;
        await InvokeAsync(ws, mine);
        await ws.SendJsonAsync(new { id = page.Save }, TestContext.Current.CancellationToken);
        await ws.ReceiveNavigationToAsync("/login?returnUrl=%2Fopen");

        Assert.Equal(1, ScriptCalls.Count(mine));
    }

    // The cookie still says administrator; the store says otherwise. Nothing may see the cookie's word first.
    [Fact]
    public async Task What_follows_the_session_s_user_never_sees_the_rights_a_connection_claims_before_they_were_asked_about()
    {
        using var host = CreateHost<FollowApp>(revalidate: true);
        var page = await OpenAsync(host, "/open", "alice");
        var users = host.Store.Peek(page.SessionId)!.Users;
        var seen = new ConcurrentQueue<string>();
        users.Changed += (_, _) => seen.Enqueue(FollowUser.Describe(users));

        host.Services.GetRequiredService<Directory>().RevokedSinceTokenIssued.Add("alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");

        Assert.Equal(["alice:user"], seen);
    }

    [Fact]
    public async Task A_connection_that_cannot_be_asked_about_leaves_the_session_holding_what_it_held()
    {
        using var host = CreateHost<FollowApp>(revalidate: true);
        var page = await OpenAsync(host, "/open", "alice");
        var session = host.Store.Peek(page.SessionId)!;
        var held = session.Users.Current;
        host.Services.GetRequiredService<Directory>().Unreachable = true;

        using var ws = await ConnectAsync(host, "alice");
        await ws.SendJsonAsync(new { type = "hello", session = page.SessionId }, TestContext.Current.CancellationToken);
        await WaitFor.True(() => Volatile.Read(ref host.Services.GetRequiredService<Directory>().Asked) > 0, "the attach asks");
        await WaitFor.True(() => !session.HasOpenTransport, "the connection is let go");

        Assert.Same(held, session.Users.Current);
        Assert.Null(session.ConnectionUser);
    }

    // A reader's action keeps a sliding sign-in alive. A page that repaints by itself in an empty room does not.
    [Fact]
    public async Task A_render_the_server_starts_itself_asks_as_nobody_s_action_and_an_event_as_the_reader_s()
    {
        var causes = new ConcurrentQueue<bool>();
        using var host = CreateHost<FollowApp>(more: services => services.AddSingleton<ISessionRevalidator>(new Noting(causes)));
        var page = await OpenAsync(host, "/open", "alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");
        causes.Clear();

        await ClickAsync(host, ws, page.Save);
        host.Store.Peek(page.SessionId)!.LastUserCheck = 0;
        host.Services.GetRequiredService<Ticker>().Tick();
        await WaitFor.True(() => causes.Count == 2, "the pushed render asks");

        Assert.Equal([false, true], causes);
    }

    private static Task InvokeAsync(System.Net.WebSockets.WebSocket ws, string argument) =>
        ws.SendJsonAsync(
            new
            {
                type = "dotNetInvoke",
                callId = Guid.NewGuid().ToString("N"),
                assemblyName = typeof(ScriptCalls).Assembly.GetName().Name,
                methodIdentifier = nameof(ScriptCalls.Note),
                argsJson = $"[\"{argument}\"]",
            },
            TestContext.Current.CancellationToken);

    // Hands the principal back, and notes whether each question was a reader's action or nobody's.
    private sealed class Noting(ConcurrentQueue<bool> unattended) : ISessionRevalidator
    {
        public ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
        {
            unattended.Enqueue(RevalidationCause.IsUnattended);
            return new(principal);
        }
    }
}

/// <summary>What a page's script called on the server, counted by the argument it passed.</summary>
public static class ScriptCalls
{
    private static readonly ConcurrentDictionary<string, int> Made = new(StringComparer.Ordinal);

    public static int Count(string argument) => Made.GetValueOrDefault(argument);

    [JSInvokable]
    public static void Note(string argument) => Made.AddOrUpdate(argument, 1, static (_, made) => made + 1);
}
