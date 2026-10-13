using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Server.Authentication;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Authentication;

/// <content>
///     A live page asks who its user is at every event, as a request through the app's auth middleware would: what
///     the answer does to the handler, to the page on screen, and to a session whose user has gone.
/// </content>
public sealed partial class SessionStaysItsUsersTests
{
    [Fact]
    public async Task Asking_who_allocates_nothing_when_the_revalidator_hands_the_same_principal_back()
    {
        using var host = CreateHost<FollowApp>(more: services => services.AddSingleton<ISessionRevalidator, SameAgain>());
        var page = await OpenAsync(host, "/open", "alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");
        var session = host.Store.Peek(page.SessionId)!;
        await AskWhoAsync(session);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var asked = RaskEndpointExtensions.TryAskWhoAsync(session, "the event", CancellationToken.None);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(asked.IsCompletedSuccessfully);
        Assert.Equal(0, allocated);
    }

    // What one task of the browser produced arrives as one frame, and is one request's worth of asking.
    [Fact]
    public async Task The_events_of_one_frame_ask_once_between_them()
    {
        using var host = CreateHost<FollowApp>(revalidate: true);
        var directory = host.Services.GetRequiredService<Directory>();
        var page = await OpenAsync(host, "/open", "alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");
        var asked = Volatile.Read(ref directory.Asked);

        await ws.SendJsonAsync(
            new { type = "batch", events = new[] { new { id = page.Save }, new { id = page.Save }, new { id = page.Save } } },
            TestContext.Current.CancellationToken);
        await WaitFor.True(() => Saves(host).Length == 3, "the three handlers run");

        Assert.Equal(1, Volatile.Read(ref directory.Asked) - asked);
    }

    [Fact]
    public async Task An_event_is_refused_when_the_revalidator_cannot_answer_and_the_next_one_asks_again()
    {
        using var host = CreateHost<FollowApp>(revalidate: true);
        var directory = host.Services.GetRequiredService<Directory>();
        var page = await OpenAsync(host, "/open", "alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");
        // The attach asks too, a moment after the socket is in place: counted before the directory goes away.
        await WaitFor.True(() => host.Store.Peek(page.SessionId)!.ConnectionUser is not null, "the attach has asked");
        var asked = Volatile.Read(ref directory.Asked);

        directory.Unreachable = true;
        await ws.SendJsonAsync(new { id = page.Save }, TestContext.Current.CancellationToken);
        await WaitFor.True(() => Volatile.Read(ref directory.Asked) > asked, "the refused event asks");
        directory.Unreachable = false;
        await ClickAsync(host, ws, page.Save);

        Assert.Equal("alice:admin", Saves(host).Single().Now);
    }

    [Fact]
    public async Task A_render_the_server_starts_itself_is_not_drawn_for_a_user_whose_sign_in_ended()
    {
        using var host = CreateHost<FollowApp>(revalidate: true);
        var page = await OpenAsync(host, "/open", "alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");
        host.Services.GetRequiredService<Directory>().SignedOut.Add("alice");
        host.Store.Peek(page.SessionId)!.LastUserCheck = 0;

        host.Services.GetRequiredService<Ticker>().Tick();
        var frame = await ws.ReceiveNavigationToAsync("/login?returnUrl=%2Fopen");

        Assert.Contains("sign in here", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("ticks=1", frame, StringComparison.Ordinal);
    }

    // Bounded, and said: a page that repaints many times a second reads the user's row once a second, not each time.
    [Fact]
    public async Task A_render_the_server_starts_within_a_second_of_the_last_answer_does_not_ask_again()
    {
        using var host = CreateHost<FollowApp>(revalidate: true);
        var directory = host.Services.GetRequiredService<Directory>();
        var page = await OpenAsync(host, "/open", "alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");
        host.Store.Peek(page.SessionId)!.LastUserCheck = Environment.TickCount64;
        var asked = Volatile.Read(ref directory.Asked);

        host.Services.GetRequiredService<Ticker>().Tick();
        await ws.ReceiveUntilAsync(static frame => frame.Contains("ticks=1", StringComparison.Ordinal), "the pushed render");

        Assert.Equal(asked, Volatile.Read(ref directory.Asked));
    }

    [Fact]
    public async Task A_right_removed_elsewhere_shows_on_the_whole_page_at_the_next_event_and_mounts_nothing()
    {
        using var host = CreateHost<FollowApp>(revalidate: true);
        var page = await OpenAsync(host, "/open", "alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");
        await ClickAsync(host, ws, page.Save);
        await ws.ReceiveUntilAsync(static frame => frame.Contains("clicks=1", StringComparison.Ordinal), "the first click's render");

        host.Services.GetRequiredService<Directory>().Admins.Clear();
        await ClickAsync(host, ws, page.Save);
        var after = await ws.ReceiveUntilAsync(static frame => frame.Contains("clicks=2", StringComparison.Ordinal), "the second click's render");

        Assert.Contains("admin panel", page.Html, StringComparison.Ordinal);
        Assert.Contains("who=alice:admin", page.Html, StringComparison.Ordinal);
        Assert.Contains("admins only", after, StringComparison.Ordinal);
        Assert.Contains("who=alice:user", after, StringComparison.Ordinal);
        Assert.Equal(["alice:admin", "alice:user"], Saves(host).Select(s => s.Now));
        Assert.Equal(1, Volatile.Read(ref host.Services.GetRequiredService<Ledger>().Mounts));
    }

    [Theory]
    [InlineData("/open")]
    [InlineData("/kept")]
    public async Task A_session_whose_sign_in_ended_goes_to_the_sign_in_page_with_the_way_back_guarded_or_not(string path)
    {
        using var host = CreateHost<FollowApp>(revalidate: true);
        var page = await OpenAsync(host, path, "alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");
        host.Services.GetRequiredService<Directory>().SignedOut.Add("alice");

        await ws.SendJsonAsync(new { id = page.Save }, TestContext.Current.CancellationToken);
        var frame = await ws.ReceiveNavigationToAsync("/login?returnUrl=" + Uri.EscapeDataString(path));

        Assert.Contains("sign in here", frame, StringComparison.Ordinal);
        Assert.Empty(Saves(host));
    }

    [Fact]
    public async Task A_navigation_by_a_session_whose_sign_in_ended_goes_to_the_sign_in_page_with_where_it_was_going()
    {
        using var host = CreateHost<FollowApp>(revalidate: true);
        var page = await OpenAsync(host, "/open", "alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");
        host.Services.GetRequiredService<Directory>().SignedOut.Add("alice");

        await ws.SendJsonAsync(new { type = "navigate", path = "/kept" }, TestContext.Current.CancellationToken);
        var frame = await ws.ReceiveNavigationToAsync("/login?returnUrl=%2Fkept");

        Assert.Contains("sign in here", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reconnect_whose_sign_in_ended_goes_to_the_sign_in_page_with_the_way_back()
    {
        using var host = CreateHost<FollowApp>(revalidate: true);
        var page = await OpenAsync(host, "/open", "alice");
        using (var first = await AttachAsync(host, page.SessionId, "alice"))
        {
            await first.CloseAndAwaitServerCleanupAsync();
        }

        host.Services.GetRequiredService<Directory>().SignedOut.Add("alice");
        using var second = await ConnectAsync(host, "alice");
        await second.SendJsonAsync(new { type = "hello", session = page.SessionId }, TestContext.Current.CancellationToken);
        var frame = await second.ReceiveNavigationToAsync("/login?returnUrl=%2Fopen");

        Assert.Contains("sign in here", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_visitor_who_never_signed_in_is_left_alone_on_a_public_page()
    {
        using var host = CreateHost<FollowApp>(revalidate: true);
        var page = await OpenAsync(host, "/open", user: null);
        using var ws = await AttachAsync(host, page.SessionId, user: null);

        await ClickAsync(host, ws, page.Save);
        var frame = await ws.ReceiveUntilAsync(static f => f.Contains("clicks=1", StringComparison.Ordinal), "the click's render");

        Assert.Equal("anon", Saves(host).Single().Now);
        Assert.DoesNotContain("/login", frame, StringComparison.Ordinal);
        Assert.Equal(0, Volatile.Read(ref host.Services.GetRequiredService<Directory>().Asked));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static async Task AskWhoAsync(LiveSession session)
    {
        for (var warm = 0; warm < 3; warm++)
        {
            await RaskEndpointExtensions.TryAskWhoAsync(session, "the event", CancellationToken.None);
        }
    }

    private static async Task<OpenedPage> OpenAsync(RaskTestHost host, string path, string? user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (user is not null)
        {
            request.Headers.Add(UserHeader, user);
        }

        using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var handler = Regex.Match(html, "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>save").Groups[1].Value;

        Assert.NotEmpty(handler);
        return new OpenedPage(MarkupAssert.SessionId(html), handler, html);
    }

    private sealed record OpenedPage(string SessionId, string Save, string Html);

    // Something the server tells its pages about with nobody asking: a clock, a feed, another user's save.
    internal sealed class Ticker
    {
        public event Action? Changed;

        public int Ticks;

        public void Tick()
        {
            Interlocked.Increment(ref Ticks);
            Changed?.Invoke();
        }
    }

    // A revalidator with nothing to report: the principal it was asked about, back.
    private sealed class SameAgain : ISessionRevalidator
    {
        public ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            new(principal);
    }
}

internal sealed partial class FollowApp : Component
{
    protected override Component? HeadAssets => Title["follow"];

    protected override Component? Render() => Router;
}

[Route("/open")]
[AllowAnonymous]
internal sealed partial class FollowOpenPage : Component
{
    protected override Component? Render() => FollowPanel;
}

[Route("/kept")]
[Authorize]
internal sealed partial class FollowKeptPage : Component
{
    protected override Component? Render() => FollowPanel;
}

[Route("/login")]
[AllowAnonymous]
internal sealed partial class FollowLoginPage : Component
{
    protected override Component? Render() => P["sign in here"];
}

// What a page shows of its user, three ways: a gate, a child that reads the principal as it renders, and a handler
// that reads it as it runs — beside state only a component that was never mounted again still holds.
internal sealed partial class FollowPanel(
    IUserProvider users, SessionStaysItsUsersTests.Ledger ledger, SessionStaysItsUsersTests.Ticker ticker) : Component
{
    private int _clicks;

    protected override Task OnMount()
    {
        Interlocked.Increment(ref ledger.Mounts);
        ticker.Changed += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        ticker.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        P[$"clicks={_clicks}"],
        P[$"ticks={Volatile.Read(ref ticker.Ticks)}"],
        FollowWho,
        Authorize.Role("admin").NotAuthorized(P["admins only"])[P["admin panel"]],
        Button.OnClick(Save)["save"]
    ];

    private void Save()
    {
        ledger.WhileSaving?.Invoke();
        _clicks++;
        ledger.Saves.Enqueue(new SessionStaysItsUsersTests.Save(FollowUser.Describe(users), "", null));
    }
}

// Reads the principal in Render and takes no parameter: nothing but a render of the whole page reaches it.
internal sealed partial class FollowWho(IUserProvider users) : Component
{
    protected override Component? Render() => P[$"who={FollowUser.Describe(users)}"];
}

internal static class FollowUser
{
    internal static string Describe(IUserProvider users) =>
        users.Current.Identity is { IsAuthenticated: true, Name: var name }
            ? $"{name}:{(users.Current.IsInRole("admin") ? "admin" : "user")}"
            : "anon";
}
