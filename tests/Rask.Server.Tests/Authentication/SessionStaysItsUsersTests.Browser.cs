using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Authentication;

/// <content>
///     What the browser's other requests tell a live session: a tab asking whether its credential changed, a post
///     that carries the credential as it is now, and the page request of a tab that lost its user.
/// </content>
public sealed partial class SessionStaysItsUsersTests
{
    private const string Same = "{\"same\":true,\"user\":true}";
    private const string NotTheSame = "{\"same\":false}";

    [Fact]
    public async Task A_tab_asking_with_the_credential_its_connection_opened_with_is_told_it_is_the_same()
    {
        using var host = CreateHost<FollowApp>();
        var page = await OpenAsync(host, "/open", "alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");

        using var answer = await WhoAsync(host, page.SessionId, "alice");

        Assert.Equal(Same, await answer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("no-store", answer.Headers.CacheControl?.ToString());
    }

    // The app issued the token again with another right, through an endpoint of its own. The open socket never saw it.
    [Fact]
    public async Task A_tab_asking_after_the_browser_s_credential_changed_is_told_it_is_not_the_same()
    {
        using var host = CreateHost<FollowApp>();
        var page = await OpenAsync(host, "/open", "alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");

        host.Services.GetRequiredService<Directory>().Admins.Clear();
        using var answer = await WhoAsync(host, page.SessionId, "alice");

        Assert.Equal(NotTheSame, await answer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // One answer for "another user's", "nobody's request for somebody's session" and "no such session": an id tells
    // a stranger nothing, and the answer carries no claim.
    [Theory]
    [InlineData("mallory", false)]
    [InlineData(null, false)]
    [InlineData("alice", true)]
    public async Task Whoever_is_not_the_session_s_user_and_whoever_names_no_session_get_the_same_answer(string? caller, bool unknownSession)
    {
        using var host = CreateHost<FollowApp>();
        var page = await OpenAsync(host, "/open", "alice");
        using var ws = await AttachAsync(host, page.SessionId, "alice");

        using var answer = await WhoAsync(host, unknownSession ? "0123456789abcdef0123456789abcdef" : page.SessionId, caller);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(NotTheSame, await answer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_tab_nobody_is_signed_in_to_is_told_it_is_the_same_and_that_it_is_nobody()
    {
        using var host = CreateHost<FollowApp>();
        var page = await OpenAsync(host, "/open", user: null);
        using var ws = await AttachAsync(host, page.SessionId, user: null);

        using var answer = await WhoAsync(host, page.SessionId, caller: null);

        Assert.Equal("{\"same\":true,\"user\":false}", await answer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Another_site_s_page_cannot_ask()
    {
        using var host = CreateHost<FollowApp>();
        var page = await OpenAsync(host, "/open", "alice");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_rask/auth/who/" + page.SessionId);
        request.Headers.Add(UserHeader, "alice");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        using var answer = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, answer.StatusCode);
    }

    // Told to ask again, not that anything changed: a tab that reopened its connection for this would do it in a loop.
    [Fact]
    public async Task A_tab_that_asks_in_a_loop_is_told_to_ask_later_after_a_handful_a_second()
    {
        using var host = CreateHost<FollowApp>();
        var page = await OpenAsync(host, "/open", "alice");
        var answers = new List<string>();

        for (var i = 0; i < 12; i++)
        {
            using var answer = await WhoAsync(host, page.SessionId, "alice");
            answers.Add($"{(int)answer.StatusCode}:{await answer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        }

        Assert.Equal("200:" + Same, answers[0]);
        Assert.Contains("200:{\"later\":true}", answers);
        Assert.DoesNotContain("200:" + NotTheSame, answers);
    }

    // A stranger is answered before anything is counted, so "later" is never theirs to hear.
    [Fact]
    public async Task A_stranger_asking_in_a_loop_is_only_ever_told_it_is_not_the_same()
    {
        using var host = CreateHost<FollowApp>();
        var page = await OpenAsync(host, "/open", "alice");
        var answers = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 12; i++)
        {
            using var answer = await WhoAsync(host, page.SessionId, "mallory");
            answers.Add(await answer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal([NotTheSame], answers);
    }

    [Fact]
    public async Task A_stranger_asking_in_a_loop_does_not_use_up_the_questions_of_the_session_s_user()
    {
        using var host = CreateHost<FollowApp>();
        var page = await OpenAsync(host, "/open", "alice");
        for (var i = 0; i < 12; i++)
        {
            using var strangers = await WhoAsync(host, page.SessionId, "mallory");
        }

        using var answer = await WhoAsync(host, page.SessionId, "alice");

        Assert.Equal(Same, await answer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // A post carries the browser's cookie as it is now; the stream it answers was opened earlier.
    [Fact]
    public async Task A_post_with_the_same_user_and_other_rights_acts_under_the_rights_it_carries()
    {
        using var host = CreateHost<FollowApp>();
        host.Http.DefaultRequestHeaders.Add(UserHeader, "alice");
        var page = await OpenAsync(host, "/open", user: null);
        await using var stream = await LiveTestConnection.OpenAsync(host, LiveTransportKind.Http, page.SessionId);
        var ledger = host.Services.GetRequiredService<Ledger>();

        host.Services.GetRequiredService<Directory>().Admins.Clear();
        await stream.SendJsonAsync(new { id = page.Save });
        await WaitFor.True(() => !ledger.Saves.IsEmpty, "the save handler runs");

        Assert.Contains("who=alice:admin", page.Html, StringComparison.Ordinal);
        Assert.Equal("alice:user", Saves(host).Single().Now);
    }

    [Fact]
    public async Task A_page_rendered_for_somebody_says_so_to_its_tab_and_one_rendered_for_nobody_does_not()
    {
        using var host = CreateHost<FollowApp>();

        var signedIn = await OpenAsync(host, "/open", "alice");
        var anonymous = await OpenAsync(host, "/open", user: null);

        Assert.Contains("data-rask-user", signedIn.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-user", anonymous.Html, StringComparison.Ordinal);
    }

    // The tab had a user, its connection was refused, and it loads its page again saying so.
    [Theory]
    [InlineData("/open")]
    [InlineData("/kept")]
    public async Task A_tab_that_lost_its_user_is_sent_to_the_sign_in_page_with_the_way_back_guarded_or_not(string path)
    {
        using var host = CreateHost<FollowApp>();

        using var response = await PageRequestAsync(host, path + "?tab=2", user: null, ended: true);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/login?returnUrl=" + Uri.EscapeDataString(path + "?tab=2"), response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), static c => c.StartsWith("rask-ended=;", StringComparison.Ordinal));
    }

    // A deploy took its session, or it is somebody else's browser now: whoever the request carries gets the page.
    [Fact]
    public async Task A_tab_that_says_it_lost_its_user_and_still_carries_one_gets_its_page()
    {
        using var host = CreateHost<FollowApp>();

        using var response = await PageRequestAsync(host, "/open", "alice", ended: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_visitor_who_never_signed_in_gets_a_public_page_as_ever()
    {
        using var host = CreateHost<FollowApp>();

        using var response = await PageRequestAsync(host, "/open", user: null, ended: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static async Task<HttpResponseMessage> WhoAsync(RaskTestHost host, string sessionId, string? caller)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_rask/auth/who/" + sessionId);
        if (caller is not null)
        {
            request.Headers.Add(UserHeader, caller);
        }

        return await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PageRequestAsync(RaskTestHost host, string path, string? user, bool ended)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (user is not null)
        {
            request.Headers.Add(UserHeader, user);
        }

        if (ended)
        {
            request.Headers.Add("Cookie", "rask-ended=1");
        }

        return await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
