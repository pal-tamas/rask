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

        var session = host.Store.Peek(page.SessionId)!;
        await ClickAsync(host, ws, page.Save);
        // The save has run, but its dispatch is still drawing: a tick now would join that dispatch's own render.
        await WaitFor.True(() => session.PendingHandlers == 0, "the click's dispatch has finished");
        await session.LastHandlerTask;
        session.LastUserCheck = 0;
        host.Services.GetRequiredService<Ticker>().Tick();
        await WaitFor.True(() => causes.Count == 2, "the pushed render asks");

        Assert.Equal([false, true], causes);
    }

    // A connection attached while the question was out. The answer is about the principal before it.
    [Fact]
    public async Task An_answer_that_arrives_after_the_session_took_another_principal_changes_nothing()
    {
        var gate = new TaskCompletionSource<ClaimsPrincipal?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = CreateHost<FollowApp>(more: services => services.AddSingleton<ISessionRevalidator>(new Waiting(gate)));
        var page = await OpenAsync(host, "/open", "alice");
        var session = host.Store.Peek(page.SessionId)!;
        var asked = RaskEndpointExtensions.TryAskWhoAsync(session, "the event", CancellationToken.None);
        var since = Directory.Principal("alice", admin: false);
        session.Users.Set(since);

        gate.SetResult(null);
        await asked;

        Assert.Same(since, session.Users.Current);
        Assert.False(session.SignInEnded);
    }

    [Fact]
    public async Task A_revalidator_that_answers_with_somebody_else_ends_the_sign_in_and_hands_the_session_to_nobody()
    {
        using var host = CreateHost<FollowApp>(
            more: services => services.AddSingleton<ISessionRevalidator>(new Answering(Directory.Principal("mallory", admin: true))));
        var page = await OpenAsync(host, "/open", "alice");
        var session = host.Store.Peek(page.SessionId)!;

        await RaskEndpointExtensions.TryAskWhoAsync(session, "the event", CancellationToken.None);

        Assert.False(session.Users.IsSignedIn);
        Assert.True(session.SignInEnded);
    }

    // One frame's events ask once between them — unless the frame runs on for longer than an answer is trusted.
    [Fact]
    public async Task The_later_events_of_a_frame_that_runs_long_ask_again()
    {
        using var host = CreateHost<FollowApp>(revalidate: true);
        var directory = host.Services.GetRequiredService<Directory>();
        var page = await OpenAsync(host, "/open", "alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");
        var session = host.Store.Peek(page.SessionId)!;
        host.Services.GetRequiredService<Ledger>().WhileSaving = () => session.LastUserCheck = 0;
        var asked = Volatile.Read(ref directory.Asked);

        await ws.SendJsonAsync(
            new { type = "batch", events = new[] { new { id = page.Save }, new { id = page.Save }, new { id = page.Save } } },
            TestContext.Current.CancellationToken);
        await WaitFor.True(() => Saves(host).Length == 3, "the three handlers run");

        Assert.Equal(3, Volatile.Read(ref directory.Asked) - asked);
    }

    // The docs' own revalidator: nobody once the token's `exp` has passed. The browser was issued a new token in
    // time, and the open socket never saw it.
    [Fact]
    public async Task A_token_issued_again_in_time_keeps_an_open_page_signed_in_past_the_first_one_s_expiry()
    {
        using var host = CreateHost<FollowApp>(more: services => services.AddSingleton<ISessionRevalidator, Lapsing>());
        var directory = host.Services.GetRequiredService<Directory>();
        directory.TokenExpires = 100;
        var page = await OpenAsync(host, "/open", "alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");

        directory.TokenExpires = 200;
        using var who = await WhoAsync(host, page.SessionId, "alice");
        directory.Now = 150;
        await ClickAsync(host, ws, page.Save);

        Assert.Equal(Same, await who.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("alice:admin", Saves(host).Single().Now);
    }

    [Fact]
    public void Only_the_first_few_refused_attaches_of_a_window_are_written_to_the_log()
    {
        RefusedAttachLines.ResetForTests();

        var written = Enumerable.Range(0, RefusedAttachLines.PerWindow + 30).Count(static _ => RefusedAttachLines.Admit());

        Assert.Equal(RefusedAttachLines.PerWindow, written);
    }

    // Over https a neighbouring subdomain can plant a plain cookie for the whole site. Only the name the browser
    // takes from this host alone is believed there.
    [Theory]
    [InlineData("https://localhost/open", "rask-ended=%2F", System.Net.HttpStatusCode.OK)]
    [InlineData("https://localhost/open", "__Host-rask-ended=%2F", System.Net.HttpStatusCode.Found)]
    [InlineData("http://localhost/open", "rask-ended=%2F", System.Net.HttpStatusCode.Found)]
    [InlineData("http://localhost/open", "rask-ended=%2Fother", System.Net.HttpStatusCode.OK)]
    [InlineData("http://localhost/open", "rask-ended=1", System.Net.HttpStatusCode.OK)]
    public async Task The_mark_of_a_tab_that_lost_its_user_is_believed_only_from_this_host_and_for_this_app(
        string url, string cookie, System.Net.HttpStatusCode expected)
    {
        using var host = CreateHost<FollowApp>();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);

        using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Under_a_path_base_the_mark_names_that_app_and_is_removed_once_used()
    {
        using var host = CreateHost<FollowApp>(pathBase: "/uj");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/uj/open");
        request.Headers.Add("Cookie", "rask-ended=%2Fuj");

        using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("/uj/login?returnUrl=%2Fopen", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), static c => c.StartsWith("rask-ended=;", StringComparison.Ordinal));
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

    // Answers when the test lets it.
    private sealed class Waiting(TaskCompletionSource<ClaimsPrincipal?> gate) : ISessionRevalidator
    {
        public ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            new(gate.Task);
    }

    // Answers every question with the same principal, whoever it was about.
    private sealed class Answering(ClaimsPrincipal answer) : ISessionRevalidator
    {
        public ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            new(answer);
    }

    // Nobody once the token has lapsed; otherwise the principal it was handed.
    private sealed class Lapsing(Directory directory) : ISessionRevalidator
    {
        public ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            new(long.Parse(principal.FindFirstValue("exp")!, System.Globalization.CultureInfo.InvariantCulture) <= directory.Now
                ? null
                : principal);
    }

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
