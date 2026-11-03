using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Wasm.Authentication;
using Rask.Wasm.Tests.Browser;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Authentication;

// The browser host's half of rask-auth-watch.ts: a tab asks its user provider again, and a sign-in that ended — the
// browser carries nobody where the page was drawn for somebody — sends the page to the sign-in page with the way back,
// whatever page it is on, with no message. A 401 from the page's own origin is how Rask's own API says so.
[Collection("WasmSession")]
public sealed class UserWatchTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    private static readonly Uri Origin = new("https://app.test/");

    [Fact]
    public async Task A_provider_that_reads_the_same_user_again_answers_the_same_and_ends_nothing()
    {
        var users = new Users(Person("ana")) { Next = Person("ana") };
        var watch = new WasmUserWatch(users, new FakeJsRuntime());

        var answer = await watch.Ask();

        Assert.Equal(WasmUserWatch.SameSomebody, answer);
        Assert.False(watch.HasEnded);
    }

    [Fact]
    public async Task A_provider_that_reads_nobody_where_there_was_somebody_ends_the_sign_in_and_empties_the_offline_cache()
    {
        var users = new Users(Person("ana")) { Next = Nobody() };
        var js = new FakeJsRuntime();
        var watch = new WasmUserWatch(users, js);
        var told = 0;
        watch.SignInEnded += (_, _) => told++;

        var answer = await watch.Ask();

        Assert.Equal(WasmUserWatch.Other, answer);
        Assert.True(watch.HasEnded);
        Assert.Equal(1, told);
        Assert.Equal(1, js.CallCount("__raskOffline.clear"));
    }

    [Fact]
    public async Task A_provider_that_reads_another_user_answers_not_the_same_and_ends_nothing()
    {
        var users = new Users(Person("ana")) { Next = Person("bob") };
        var watch = new WasmUserWatch(users, new FakeJsRuntime());

        var answer = await watch.Ask();

        Assert.Equal(WasmUserWatch.Other, answer);
        Assert.False(watch.HasEnded);
    }

    [Fact]
    public async Task A_provider_that_cannot_be_reached_gives_no_answer_and_ends_nothing()
    {
        var users = new Users(Person("ana")) { Fails = true };
        var watch = new WasmUserWatch(users, new FakeJsRuntime());

        var answer = await watch.Ask();

        Assert.Equal(WasmUserWatch.Unanswered, answer);
        Assert.False(watch.HasEnded);
    }

    [Fact]
    public async Task The_anonymous_default_has_nobody_to_watch_and_is_never_asked()
    {
        var watch = new WasmUserWatch(new AnonymousUserProvider(), new FakeJsRuntime());

        var answer = await watch.Ask();

        Assert.Equal(WasmUserWatch.Off, answer);
    }

    [Fact]
    public async Task Two_questions_at_once_ask_the_provider_once()
    {
        var users = new Users(Person("ana")) { Next = Person("ana"), Gate = new TaskCompletionSource() };
        var watch = new WasmUserWatch(users, new FakeJsRuntime());

        var first = watch.Ask();
        var second = watch.Ask();
        users.Gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, users.Refreshes);
    }

    [Fact]
    public async Task A_401_from_the_pages_own_origin_ends_the_sign_in_of_a_tab_whose_browser_carries_nobody_now()
    {
        var users = new Users(Person("ana")) { Next = Nobody() };
        var (http, watch) = Client(users, HttpStatusCode.Unauthorized);

        using var response = await http.GetAsync(new Uri("api/orders", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(watch.HasEnded);
        Assert.Equal(1, users.Refreshes);
    }

    [Fact]
    public async Task A_401_while_the_provider_still_reads_the_user_ends_nothing()
    {
        var users = new Users(Person("ana")) { Next = Person("ana") };
        var (http, watch) = Client(users, HttpStatusCode.Unauthorized);

        using var response = await http.GetAsync(new Uri("api/orders", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.False(watch.HasEnded);
        Assert.Equal(1, users.Refreshes);
    }

    [Fact]
    public async Task A_401_from_another_origin_asks_nothing()
    {
        var users = new Users(Person("ana")) { Next = Nobody() };
        var (http, watch) = Client(users, HttpStatusCode.Unauthorized);

        using var response = await http.GetAsync(new Uri("https://elsewhere.test/api/orders"), TestContext.Current.CancellationToken);

        Assert.False(watch.HasEnded);
        Assert.Equal(0, users.Refreshes);
    }

    [Fact]
    public async Task A_401_to_a_tab_that_had_nobody_asks_nothing()
    {
        var users = new Users(Nobody()) { Next = Nobody() };
        var (http, watch) = Client(users, HttpStatusCode.Unauthorized);

        using var response = await http.GetAsync(new Uri("auth/login", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.False(watch.HasEnded);
        Assert.Equal(0, users.Refreshes);
    }

    [Fact]
    public async Task Any_other_answer_from_the_pages_own_origin_asks_nothing()
    {
        var users = new Users(Person("ana")) { Next = Nobody() };
        var (http, watch) = Client(users, HttpStatusCode.Forbidden);

        using var response = await http.GetAsync(new Uri("api/orders", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.False(watch.HasEnded);
        Assert.Equal(0, users.Refreshes);
    }

    [Fact]
    public async Task A_tab_whose_sign_in_ended_goes_from_a_public_page_to_the_sign_in_page_with_the_way_back()
    {
        var (session, users, watch, route) = Open("/uw/desk?tab=2");
        await session.InitialRenderAsync();
        users.Next = Nobody();

        await watch.Ask();
        await WaitFor.True(() => route.Path == "/login", "the render the ended sign-in asked for");

        Assert.Equal("/uw/desk?tab=2", route.Query["returnUrl"].ToString());
        Assert.False(watch.HasEnded);
    }

    [Fact]
    public async Task An_event_on_a_page_whose_sign_in_ended_runs_nothing_and_answers_with_the_sign_in_page()
    {
        var (session, users, watch, _) = Open("/uw/desk");
        var html = Html(await session.InitialRenderAsync());
        UserWatchDesk.Acted = 0;

        // As the watch leaves it, before the render it asked for has run.
        users.Become(Nobody());
        watch.HasEnded = true;
        var frame = await Click(session, html, "act");

        Assert.Equal(0, UserWatchDesk.Acted);
        Assert.Equal(("/login?returnUrl=%2Fuw%2Fdesk", "replace"), History(frame));
    }

    [Fact]
    public async Task A_tab_already_on_the_sign_in_page_stays_there_when_its_sign_in_ended()
    {
        var (session, _, watch, _) = Open("/login");
        await session.InitialRenderAsync();

        watch.HasEnded = true;
        var frame = await session.DispatchAsync("""{"type":"navigate","path":"/login","query":""}"""u8.ToArray());

        Assert.NotEqual("replace", History(frame).Action);
        Assert.Contains("sign-in-here", Html(frame), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tab_signed_in_again_before_its_page_was_drawn_stays_where_it_is()
    {
        var (session, _, watch, _) = Open("/uw/desk");
        await session.InitialRenderAsync();

        watch.HasEnded = true;
        var frame = await session.DispatchAsync("""{"type":"navigate","path":"/uw/desk","query":""}"""u8.ToArray());

        Assert.NotEqual("replace", History(frame).Action);
        Assert.Contains("desk-of-ana", Html(frame), StringComparison.Ordinal);
        Assert.False(watch.HasEnded);
    }

    private (WasmLiveSession Session, Users Users, WasmUserWatch Watch, RouteState Route) Open(string path)
    {
        var users = new Users(Person("ana")) { Next = Person("ana") };
        var (session, services) = WasmSessionHarness.NewSession<UserWatchApp>(
            configure: s => s.AddLogging().AddAuthorizationCore()
                .AddSingleton<IUserProvider>(users)
                .AddSingleton<IJSRuntime>(new FakeJsRuntime())
                .AddSingleton<WasmUserWatch>(),
            diffMode: DiffMode);
        var route = services.GetRequiredService<RouteState>();
        var at = path.IndexOf('?', StringComparison.Ordinal);
        route.Path = at < 0 ? path : path[..at];
        route.Query = at < 0 ? QueryCollection.Empty : QueryString.Parse(path[at..]);
        return (session, users, services.GetRequiredService<WasmUserWatch>(), route);
    }

    private static (HttpClient Http, WasmUserWatch Watch) Client(Users users, HttpStatusCode status)
    {
        var watch = new WasmUserWatch(users, new FakeJsRuntime());
        var services = new ServiceCollection().AddSingleton(watch).BuildServiceProvider();
        var http = new HttpClient(new SignInRefusals(services, Origin) { InnerHandler = new Answers(status) })
        {
            BaseAddress = Origin
        };
        return (http, watch);
    }

    private static ClaimsPrincipal Person(string name) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "test"));

    private static ClaimsPrincipal Nobody() => new(new ClaimsIdentity());

    private static async Task<byte[]> Click(WasmLiveSession session, string html, string buttonId)
    {
        var handler = Regex.Match(html, $"id=\"{buttonId}\" data-rask-on-click=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEqual(string.Empty, handler);
        return await session.DispatchAsync(Encoding.UTF8.GetBytes($$"""{"id":"{{handler}}","type":"click"}"""));
    }

    private static string Html(byte[] frame)
    {
        using var doc = JsonDocument.Parse(frame.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }

    private static (string? Url, string? Action) History(byte[] frame)
    {
        using var doc = JsonDocument.Parse(frame.AsMemory());
        return doc.RootElement.TryGetProperty("history", out var history)
            ? (history.GetProperty("url").GetString(), history.GetProperty("action").GetString())
            : (null, null);
    }

    // A user provider whose next read is set by the test, as a server's answer would be.
    private sealed class Users(ClaimsPrincipal current) : IUserProvider
    {
        public ClaimsPrincipal Current { get; private set; } = current;

        public ClaimsPrincipal? Next { get; set; }

        public bool Fails { get; init; }

        public TaskCompletionSource? Gate { get; init; }

        public int Refreshes { get; private set; }

        // The user changes with nobody told, so no render races the test.
        public void Become(ClaimsPrincipal user) => Current = user;

        public event EventHandler? Changed;

        public async Task Refresh()
        {
            Refreshes++;
            if (Gate is not null)
            {
                await Gate.Task;
            }

            if (Fails)
            {
                throw new HttpRequestException("offline");
            }

            if (Next is { } next && !ReferenceEquals(next, Current))
            {
                Current = next;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private sealed class Answers(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}

internal sealed partial class UserWatchApp : Component
{
    protected override Component? Render() => Router;
}

[Route("/uw/desk")]
internal sealed partial class UserWatchDesk(IUserProvider users) : Component
{
    public static int Acted { get; set; }

    protected override Component? Render() =>
    [
        P[$"desk-of-{users.Current.Identity?.Name ?? "nobody"}"],
        Button.Id("act").OnClick(() => Acted++)["act"]
    ];
}

[Route("/login")]
internal sealed partial class UserWatchSignIn : Component
{
    protected override Component? Render() => P["sign-in-here"];
}
