#pragma warning disable RASK014 // the tests render the very instance they hand to the root

using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

// A navigation made while a page is being rendered is a redirect: the host takes it apart from one a handler
// made, follows it a bounded number of times, and refuses the one a hook makes after an await.
public sealed partial class NavigatorRedirectTests
{
    [Fact]
    public void A_navigation_made_while_a_page_mounts_is_a_redirect_the_host_can_take()
    {
        var nav = new Navigator(new RouteState());
        var page = new Redirecting { Hook = () => nav.NavigateTo("/list") };

        using var handler = nav.EnterHandler();
        page.RenderAsLiveRoot();
        var pending = nav.RedirectPending;
        var taken = nav.TryConsumeRedirect(out var url, out _);

        Assert.True(pending);
        Assert.True(taken);
        Assert.Equal("/list", url);
        Assert.False(nav.RedirectPending);
    }

    [Fact]
    public void A_redirect_its_host_never_took_is_no_longer_pending_once_the_handler_has_ended()
    {
        var nav = new Navigator(new RouteState());
        var page = new Redirecting { Hook = () => nav.NavigateTo("/list") };

        using (nav.EnterHandler())
        {
            page.RenderAsLiveRoot();
        }

        Assert.False(nav.RedirectPending);
    }

    [Fact]
    public void A_navigation_a_handler_makes_is_not_a_redirect()
    {
        var nav = new Navigator(new RouteState());

        using (nav.EnterHandler())
        {
            nav.NavigateTo("/list");
        }

        Assert.False(nav.RedirectPending);
        Assert.False(nav.TryConsumeRedirect(out _, out _));
        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/list", url);
    }

    [Fact]
    public void The_redirect_after_the_tenth_in_a_row_throws_where_the_page_navigates()
    {
        var nav = new Navigator(new RouteState());
        InvalidOperationException? refused = null;

        using (nav.EnterHandler())
        {
            for (var hop = 0; hop <= Navigator.MaxRedirects; hop++)
            {
                new Redirecting { Hook = () => refused = Record.Exception(() => nav.NavigateTo("/next")) as InvalidOperationException }
                    .RenderAsLiveRoot();
                nav.TryConsumeRedirect(out _, out _);
            }
        }

        Assert.StartsWith("Too many redirects", refused?.Message);
    }

    [Fact]
    public void A_new_handler_starts_the_count_of_redirects_again()
    {
        var nav = new Navigator(new RouteState());
        using (nav.EnterHandler())
        {
            for (var hop = 0; hop < Navigator.MaxRedirects; hop++)
            {
                new Redirecting { Hook = () => nav.NavigateTo("/next") }.RenderAsLiveRoot();
                nav.TryConsumeRedirect(out _, out _);
            }
        }

        using (nav.EnterHandler())
        {
            new Redirecting { Hook = () => nav.NavigateTo("/list") }.RenderAsLiveRoot();
        }

        Assert.True(nav.TryConsumeRedirect(out var url, out _));
        Assert.Equal("/list", url);
    }

    [Fact]
    public async Task A_hook_that_navigates_after_an_await_hands_the_navigation_to_its_session()
    {
        var state = new RouteState();
        var nav = new Navigator(state);
        var session = new Session();
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new Redirecting
        {
            RenderHandle = session,
            Later = async () =>
            {
                await Task.Yield();
                nav.NavigateTo("/list");
                resumed.SetResult();
            },
        };
        using (nav.EnterHandler())
        {
            page.RenderAsLiveRoot();
        }

        await resumed.Task;
        var movedBeforeTheSessionsTurn = state.Path;
        using (nav.EnterHandler())
        {
            session.Handed.Single()();
        }

        Assert.Equal("/", movedBeforeTheSessionsTurn);
        Assert.True(nav.TryConsumeHistory(out var url, out var replace));
        Assert.Equal("/list", url);
        Assert.True(replace);
    }

    [Fact]
    public void A_page_that_navigates_as_it_mounts_with_no_dispatch_behind_it_is_withheld_and_handed_to_its_session()
    {
        var state = new RouteState();
        var nav = new Navigator(state);
        var session = new Session();
        var page = new Redirecting { RenderHandle = session, Hook = () => nav.NavigateTo("/list") };

        page.RenderAsLiveRoot();
        var withheld = nav.RedirectPending;
        using var turn = nav.EnterHandler();
        session.Handed.Single()();

        Assert.True(withheld);
        Assert.False(nav.RedirectPending);
        Assert.Equal("/list", state.Path);
    }

    [Fact]
    public void A_navigation_handed_over_by_a_page_that_has_since_left_is_dropped()
    {
        var state = new RouteState();
        var nav = new Navigator(state);
        var session = new Session();
        var page = new Redirecting { RenderHandle = session, Hook = () => nav.NavigateTo("/list") };
        page.RenderAsLiveRoot();

        ComponentLifecycle.DisposeComponentTree(page);
        using var turn = nav.EnterHandler();
        session.Handed.Single()();

        Assert.Equal("/", state.Path);
        Assert.False(nav.TryConsumeHistory(out _, out _));
    }

    [Fact]
    public void Navigations_handed_over_one_after_another_count_toward_the_limit_of_redirects()
    {
        var nav = new Navigator(new RouteState());
        var session = new Session();
        InvalidOperationException? refused = null;

        for (var hop = 0; hop <= Navigator.MaxRedirects && refused is null; hop++)
        {
            var page = new Redirecting
            {
                RenderHandle = session,
                Hook = () => refused = Record.Exception(() => nav.NavigateTo("/next")) as InvalidOperationException,
            };
            page.RenderAsLiveRoot();
            if (refused is null)
            {
                using var turn = nav.EnterHandler();
                session.Handed[^1]();
                nav.TryConsumeHistory(out _, out _);
            }
        }

        Assert.StartsWith("Too many redirects", refused?.Message);
        Assert.Equal(Navigator.MaxRedirects, session.Handed.Count);
    }

    [Fact]
    public async Task A_hook_that_navigates_after_an_await_during_the_first_request_still_redirects_it()
    {
        var nav = new Navigator(new RouteState());
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new Redirecting
        {
            Later = async () =>
            {
                await Task.Yield();
                nav.NavigateTo("/list");
                resumed.SetResult();
            },
        };

        using (nav.EnterInitialRender())
        {
            page.RenderAsLiveRoot();
            await resumed.Task;
        }

        Assert.True(nav.TryConsumeHistory(out var url, out _));
        Assert.Equal("/list", url);
    }

    [Fact]
    public void Navigation_is_refused_again_once_the_first_request_has_been_answered()
    {
        var nav = new Navigator(new RouteState());

        nav.EnterInitialRender().Dispose();

        Assert.Throws<InvalidOperationException>(() => nav.NavigateTo("/list"));
    }

    // Stands in for the session: it keeps what it is handed, for the test to run as the session's own turn.
    private sealed class Session : IRenderHandle
    {
        public List<Action> Handed { get; } = [];

        public Task RequestRender() => Task.CompletedTask;

        bool IRenderHandle.TryNavigate(Action navigate)
        {
            Handed.Add(navigate);
            return true;
        }
    }

    private sealed partial class Redirecting : Component
    {
        public Action? Hook { get; init; }

        public Func<Task>? Later { get; init; }

        protected override Task OnMount()
        {
            Hook?.Invoke();
            return Later?.Invoke() ?? Task.CompletedTask;
        }

        protected override Component? Render() => P["redirecting"];
    }
}
