#pragma warning disable RASK014 // the tests render the very instance they hand to the root

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
    public async Task A_hook_that_navigates_after_an_await_is_refused_even_while_the_handler_is_still_running()
    {
        var nav = new Navigator(new RouteState());
        var resumed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new Redirecting
        {
            Later = async () =>
            {
                await Task.Yield();
                resumed.SetResult(Record.Exception(() => nav.NavigateTo("/list")));
            },
        };

        Exception? refused;
        using (nav.EnterHandler())
        {
            page.RenderAsLiveRoot();
            refused = await resumed.Task;
        }

        Assert.Contains("after an await", Assert.IsType<InvalidOperationException>(refused).Message);
        Assert.False(nav.TryConsumeHistory(out _, out _));
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
