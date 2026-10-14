using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Components;
using Rask.Server.Authentication;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Authentication;

/// <summary>
///     What ties a live session to the user it acts for, and for how long: who may drive it, and which principal its
///     handlers see after the user's rights changed.
/// </summary>
/// <remarks>
///     The host here signs a request in from a header and reads the user's rights from a directory, the way a
///     hand-wired host's token validation does. <see cref="HelloOwnershipTests" /> covers who may attach to a session
///     that has an owner. These cover what an attach does to the principal, the sessions whose owner has gone, and the
///     endpoints beside the socket.
/// </remarks>
public sealed partial class SessionStaysItsUsersTests
{
    private const string UserHeader = "X-Test-User";

    [Fact]
    public async Task A_same_user_reconnect_makes_handlers_act_under_the_rights_the_new_connection_carries()
    {
        using var host = CreateHost();
        var (sessionId, save) = await OpenPageAsync(host, "alice");
        using (var first = await AttachAsync(host, sessionId, "alice"))
        {
            await first.CloseAndAwaitServerCleanupAsync();
        }

        host.Services.GetRequiredService<Directory>().Admins.Clear();
        using var second = await AttachAsync(host, sessionId, "alice");
        await ClickAsync(host, second, save);

        Assert.Equal("alice:user", Saves(host).Single().Now);
    }

    // A host that signs in only the requests under its path base: the socket is one of them, because the path base is
    // part of its route and not moved into Request.PathBase.
    [Fact]
    public async Task Under_a_path_base_the_socket_is_a_request_under_it_and_carries_the_user_as_the_page_does()
    {
        using var host = CreateHost(pathBase: "/uj");
        var (sessionId, save) = await OpenPageAsync(host, "alice", "/uj");
        host.Services.GetRequiredService<Directory>().Admins.Clear();

        using var ws = await AttachAsync(host, sessionId, "alice");
        await ClickAsync(host, ws, save);

        Assert.Equal(new Save("alice:user", "alice:admin", "alice"), Saves(host).Single());
    }

    // Stated, not endorsed: the reconnect replaces the principal and re-checks the route's guard, and mounts nothing.
    [Fact]
    public async Task What_a_page_read_from_the_principal_as_it_mounted_is_kept_across_a_same_user_reconnect()
    {
        using var host = CreateHost();
        var (sessionId, save) = await OpenPageAsync(host, "alice");
        host.Services.GetRequiredService<Directory>().Admins.Clear();

        using var ws = await AttachAsync(host, sessionId, "alice");
        await ClickAsync(host, ws, save);

        Assert.Equal("alice:admin", Saves(host).Single().Mounted);
    }

    // Stated, not endorsed: a socket carries the principal of its upgrade request, and no later request reaches it.
    [Fact]
    public async Task An_open_socket_keeps_the_rights_it_opened_with_when_the_app_registers_no_revalidator()
    {
        using var host = CreateHost();
        var (sessionId, save) = await OpenPageAsync(host, "alice");
        using var ws = await AttachAsync(host, sessionId, "alice");

        host.Services.GetRequiredService<Directory>().Admins.Clear();
        await ClickAsync(host, ws, save);

        Assert.Equal("alice:admin", Saves(host).Single().Now);
    }

    [Fact]
    public async Task An_open_socket_loses_a_revoked_right_at_its_next_handler_when_a_revalidator_is_registered()
    {
        using var host = CreateHost(revalidate: true);
        var (sessionId, save) = await OpenPageAsync(host, "alice");
        using var ws = await AttachAsync(host, sessionId, "alice");

        host.Services.GetRequiredService<Directory>().Admins.Clear();
        await ClickAsync(host, ws, save);

        Assert.Equal("alice:user", Saves(host).Single().Now);
    }

    // The cookie or token a reconnect carries can outlive the sign-in behind it. The reconnect is checked before it
    // acts, not up to thirty seconds later.
    [Fact]
    public async Task A_reconnect_whose_sign_in_has_ended_does_not_act_as_that_user_again()
    {
        using var host = CreateHost(revalidate: true);
        var directory = host.Services.GetRequiredService<Directory>();
        var (sessionId, save) = await OpenPageAsync(host, "alice");
        using (var first = await AttachAsync(host, sessionId, "alice"))
        {
            await ClickAsync(host, first, save);
            await first.CloseAndAwaitServerCleanupAsync();
        }

        directory.SignedOut.Add("alice");
        using var second = await AttachAsync(host, sessionId, "alice");
        await ClickAsync(host, second, save);

        Assert.Equal(["alice:admin", "anon"], Saves(host).Select(s => s.Now));
    }

    [Fact]
    public async Task A_reconnect_is_given_the_rights_the_revalidator_names_before_it_acts()
    {
        using var host = CreateHost(revalidate: true);
        var directory = host.Services.GetRequiredService<Directory>();
        var (sessionId, save) = await OpenPageAsync(host, "alice");
        using (var first = await AttachAsync(host, sessionId, "alice"))
        {
            await ClickAsync(host, first, save);
            await first.CloseAndAwaitServerCleanupAsync();
        }

        directory.RevokedSinceTokenIssued.Add("alice");
        using var second = await AttachAsync(host, sessionId, "alice");
        await ClickAsync(host, second, save);

        Assert.Equal(["alice:admin", "alice:user"], Saves(host).Select(s => s.Now));
    }

    // A session whose user was signed out has no owner left to compare with, and still holds what it mounted for them.
    [Fact]
    public async Task A_session_whose_user_was_signed_out_is_not_adopted_by_another_signed_in_user()
    {
        using var host = CreateHost();
        var (sessionId, _) = await OpenPageAsync(host, "alice");
        host.Store.Peek(sessionId)!.Services.GetRequiredService<SessionUserProvider>().Clear();

        using var ws = await ConnectAsync(host, "mallory");
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, TestContext.Current.CancellationToken);
        var reply = await ws.ReceiveTextAsync();

        Assert.Contains("\"status\":\"unknown\"", reply, StringComparison.Ordinal);
        Assert.Equal(0, host.Store.ConnectedCount);
    }

    [Fact]
    public async Task A_session_whose_user_was_signed_out_is_reattached_by_that_user_and_by_nobody()
    {
        using var host = CreateHost();
        var (sessionId, _) = await OpenPageAsync(host, "alice");
        host.Store.Peek(sessionId)!.Services.GetRequiredService<SessionUserProvider>().Clear();

        using (var anonymous = await AttachAsync(host, sessionId, user: null))
        {
            await anonymous.CloseAndAwaitServerCleanupAsync();
        }

        using var alice = await AttachAsync(host, sessionId, "alice");

        Assert.Equal(1, host.Store.ConnectedCount);
    }

    [Theory]
    [InlineData("POST", "/_rask/upload/")]
    [InlineData("GET", "/_rask/download/")]
    public async Task A_file_endpoint_refuses_another_signed_in_user(string method, string path)
    {
        using var host = CreateHost();
        var (sessionId, _) = await OpenPageAsync(host, "alice");

        using var response = await FileRequestAsync(host, method, path, sessionId, "mallory");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/_rask/upload/")]
    [InlineData("GET", "/_rask/download/")]
    public async Task A_file_endpoint_refuses_another_user_once_the_session_s_own_was_signed_out(string method, string path)
    {
        using var host = CreateHost();
        var (sessionId, _) = await OpenPageAsync(host, "alice");
        host.Store.Peek(sessionId)!.Services.GetRequiredService<SessionUserProvider>().Clear();

        using var response = await FileRequestAsync(host, method, path, sessionId, "mallory");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_post_refuses_another_user_once_the_session_s_own_was_signed_out()
    {
        using var host = CreateHost();
        var (sessionId, save) = await OpenPageAsync(host, "alice");
        host.Store.Peek(sessionId)!.Services.GetRequiredService<SessionUserProvider>().Clear();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/_rask/send/{sessionId}")
        {
            Content = JsonContent.Create(new[] { new { id = save } }),
        };
        request.Headers.Add(UserHeader, "mallory");
        request.Headers.Add("Rask-Stream", "1");

        using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Stated, not endorsed: Rask knows a user only as the request's own principal. One the app works out for itself
    // leaves the session with no owner, so the id alone attaches to it.
    [Fact]
    public async Task A_user_the_app_resolved_for_itself_does_not_make_the_session_theirs()
    {
        using var host = CreateHost(stampUser: false);
        var (sessionId, save) = await OpenPageAsync(host, "alice");

        using var ws = await AttachAsync(host, sessionId, "mallory");
        await ClickAsync(host, ws, save);

        Assert.Equal(new Save("anon", "anon", "alice"), Saves(host).Single());
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static Save[] Saves(RaskTestHost host) => [.. host.Services.GetRequiredService<Ledger>().Saves];

    private static async Task<(string SessionId, string SaveHandler)> OpenPageAsync(RaskTestHost host, string user, string path = "/")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(UserHeader, user);
        using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var handler = Regex.Match(html, "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>save").Groups[1].Value;

        Assert.NotEmpty(handler);
        return (MarkupAssert.SessionId(html), handler);
    }

    private static Task<WebSocket> ConnectAsync(RaskTestHost host, string? user)
    {
        host.WebSockets.ConfigureRequest = request =>
        {
            if (user is not null)
            {
                request.Headers[UserHeader] = user;
            }
        };
        return host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);
    }

    // Returns once the attach has adopted what the connection carries, not only once its socket is in place: the
    // revalidator is asked a moment after that, and what these tests count, watch or replace follows from its answer.
    private static async Task<WebSocket> AttachAsync(RaskTestHost host, string sessionId, string? user)
    {
        var session = host.Store.Peek(sessionId);
        var before = session?.ConnectionUser;
        var ws = await ConnectAsync(host, user);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        if (session is not null)
        {
            // Every request is signed in afresh, so the adopted principal is never the one a connection before it brought.
            await WaitFor.True(() => !ReferenceEquals(session.ConnectionUser, before), "the attach adopts the connection's user");
        }

        return ws;
    }

    private static async Task ClickAsync(RaskTestHost host, WebSocket ws, string handler)
    {
        var ledger = host.Services.GetRequiredService<Ledger>();
        var before = ledger.Saves.Count;
        await ws.SendJsonAsync(new { id = handler }, TestContext.Current.CancellationToken);
        await WaitFor.True(() => ledger.Saves.Count > before, "the save handler runs");
    }

    private static async Task<HttpResponseMessage> FileRequestAsync(
        RaskTestHost host, string method, string path, string sessionId, string user)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path + sessionId + (method == "GET" ? "/token" : ""));
        request.Headers.Add(UserHeader, user);
        if (method == "POST")
        {
            request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "data.bin" } };
        }

        return await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static RaskTestHost CreateHost(bool revalidate = false, bool stampUser = true, string pathBase = "") =>
        CreateHost<OwnedPageApp>(revalidate, stampUser, pathBase);

    private static RaskTestHost CreateHost<TApp>(
        bool revalidate = false, bool stampUser = true, string pathBase = "", Action<IServiceCollection>? more = null)
        where TApp : Component =>
        RaskTestHost.Create<TApp>(
            services =>
            {
                services.AddSingleton<Directory>();
                services.AddSingleton<Ledger>();
                services.AddSingleton<Ticker>();
                services.AddHttpContextAccessor();
                if (revalidate)
                {
                    services.AddSingleton<ISessionRevalidator, DirectoryRevalidator>();
                }

                more?.Invoke(services);
            },
            app =>
            {
                if (stampUser)
                {
                    app.Use((ctx, next) => StampUserAsync(ctx, next, pathBase));
                }
            }, pathBase: pathBase, diffMode: Rask.Core.Live.LiveDiffMode.DisabledFull);

    // Signs the request in as whoever the header names, with the rights the directory holds for them at that moment.
    private static Task StampUserAsync(HttpContext ctx, RequestDelegate next, string pathBase)
    {
        if (ctx.Request.Path.StartsWithSegments(pathBase) && ctx.Request.Headers.TryGetValue(UserHeader, out var name) && !string.IsNullOrEmpty(name))
        {
            var directory = ctx.RequestServices.GetRequiredService<Directory>();
            ctx.User = directory.PrincipalNow(name!);
        }

        return next(ctx);
    }

    internal sealed record Save(string Now, string Mounted, string? AppResolved);

    internal sealed class Ledger
    {
        public ConcurrentQueue<Save> Saves { get; } = new();

        public int Mounts;

        // Run by the page's save handler, for a test that has to do something while a handler runs.
        public Action? WhileSaving { get; set; }
    }

    // The app's user store: who is an administrator, whose sign-in has ended, and whose right was taken away after
    // their token was issued — which the token still claims.
    internal sealed class Directory
    {
        public ConcurrentBag<string> SignedOut { get; } = [];

        public ConcurrentBag<string> RevokedSinceTokenIssued { get; } = [];

        public HashSet<string> Admins { get; } = ["alice"];

        // Counted, so a test can wait for the question it is about to change the answer to.
        public int Asked;

        // The store cannot be reached: asking throws.
        public bool Unreachable { get; set; }

        // When the token a request carries lapses, as its `exp` claim, and what time it is — for a host whose
        // revalidator reads the one against the other.
        public long? TokenExpires { get; set; }

        public long Now { get; set; }

        public ClaimsPrincipal PrincipalNow(string name)
        {
            var principal = Principal(name, Admins.Contains(name));
            if (TokenExpires is { } expires)
            {
                ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("exp", expires.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            return principal;
        }

        public static ClaimsPrincipal Principal(string name, bool admin) =>
            new(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, name), new Claim(ClaimTypes.Name, name),
                    new Claim("isAdmin", admin ? "true" : "false"), new Claim(ClaimTypes.Role, admin ? "admin" : "user"),
                ],
                "Test"));
    }

    private sealed class DirectoryRevalidator(Directory directory) : ISessionRevalidator
    {
        public ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref directory.Asked);
            if (directory.Unreachable)
            {
                throw new InvalidOperationException("the user store is unreachable");
            }

            var name = principal.Identity!.Name!;
            return ValueTask.FromResult(
                directory.SignedOut.Contains(name)
                    ? null
                    : Directory.Principal(name, directory.Admins.Contains(name) && !directory.RevokedSinceTokenIssued.Contains(name)));
        }
    }

    internal sealed partial class OwnedPageApp(IUserProvider users, Ledger ledger, IHttpContextAccessor http) : Component
    {
        private string _mounted = "";
        private string? _appResolved;

        protected override Component? HeadAssets => Markup.Title["owned"];

        protected override Task OnMount()
        {
            _mounted = Describe();
            _appResolved = http.HttpContext?.Request.Headers[UserHeader].ToString();
            return Task.CompletedTask;
        }

        protected override Component? Render() =>
        [
            Markup.P[$"saves={ledger.Saves.Count}"],
            Button.OnClick(() => ledger.Saves.Enqueue(new Save(Describe(), _mounted, _appResolved)))["save"]
        ];

        private string Describe() =>
            users.Current.Identity is { IsAuthenticated: true, Name: var name }
                ? $"{name}:{(users.Current.HasClaim("isAdmin", "true") ? "admin" : "user")}"
                : "anon";
    }
}
