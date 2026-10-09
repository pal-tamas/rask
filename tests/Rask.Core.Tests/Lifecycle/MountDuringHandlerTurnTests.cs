using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

// A handler makes its owner's lifetime the ambient cancellation for the calls that take no token. A render
// that happens during that handler's turn mounts and updates OTHER components, and each of those loads under
// its own lifetime — the handler's owner is often the very component the render is about to unmount.
[Collection("RouteRegistry")]
public sealed partial class MountDuringHandlerTurnTests : global::Rask.Core.RaskMarkup
{
    private static readonly Route[] Routes = [Route.To<FormPage>("/form"), Route.To<ListPage>("/list")];

    [Theory]
    [InlineData(HandlerShape.GoLast)]
    [InlineData(HandlerShape.GoThenAwait)]
    [InlineData(HandlerShape.GoFirst)]
    public async Task A_page_reached_from_a_handler_loads_both_of_its_reads(HandlerShape shape)
    {
        using var turn = new Turn("/form", shape);

        await turn.Click();
        await turn.Shows("rows:3 total:3");

        Assert.Null(turn.Probe.ListFault);
        Assert.Equal(turn.Probe.ListLifetime, turn.Probe.ListFirstRead);
        Assert.Equal(turn.Probe.ListLifetime, turn.Probe.ListSecondRead);
    }

    [Fact]
    public async Task A_page_reached_from_a_handler_under_a_handler_timeout_reads_its_own_token()
    {
        using var timeout = new CancellationTokenSource();
        using var turn = new Turn("/form", HandlerShape.GoLast) { HandlerTimeout = timeout.Token };

        await turn.Click();
        await turn.Shows("rows:3 total:3");

        Assert.Equal(turn.Probe.ListLifetime, turn.Probe.ListFirstRead);
        Assert.Equal(turn.Probe.ListLifetime, turn.Probe.ListOwnTokenInMount);
    }

    [Fact]
    public async Task The_rest_of_a_handler_after_navigating_away_is_still_cancelled_with_its_own_component()
    {
        using var turn = new Turn("/form", HandlerShape.GoThenAwait);

        await turn.Click();
        await turn.Shows("rows:3 total:3");

        Assert.Equal(turn.Probe.FormLifetime, turn.Probe.HandlerTokenAfterGo);
        Assert.True(turn.Probe.HandlerTokenAfterGo.IsCancellationRequested);
    }

    [Fact]
    public async Task A_handler_leaves_no_token_behind_when_it_throws()
    {
        using var turn = new Turn("/form", HandlerShape.Throws);

        await turn.Click();

        Assert.Contains("boundary-tripped", turn.Html);
        Assert.False(Ambient.CancellationToken.CanBeCanceled);
        Assert.Null(Ambient.Services);
    }

    [Fact]
    public async Task A_child_mounted_by_a_click_loads_under_its_own_lifetime()
    {
        using var turn = new Turn("/form", HandlerShape.OpenPanel);

        await turn.Click();
        await turn.Shows("panel:loaded");

        Assert.Equal(turn.Probe.PanelLifetime, turn.Probe.PanelRead);
        Assert.NotEqual(turn.Probe.FormLifetime, turn.Probe.PanelRead);
    }

    [Fact]
    public async Task A_parents_callback_run_from_a_childs_handler_is_cancelled_with_the_parent_not_the_child()
    {
        using var turn = new Turn("/form", HandlerShape.CallbackClosesChild);

        await turn.Click();
        await turn.Shows("closed");

        Assert.Equal(turn.Probe.FormLifetime, turn.Probe.CallbackTokenAfterClose);
        Assert.NotEqual(turn.Probe.EditorLifetime, turn.Probe.CallbackTokenAfterClose);
        Assert.False(turn.Probe.CallbackTokenAfterClose.IsCancellationRequested);
    }

    // A callback built by hand, with no chain step to record who wrote it, is nobody's: it runs for its invoker.
    [Fact]
    public async Task A_callback_built_by_hand_around_a_bare_delegate_is_still_cancelled_with_its_invoker()
    {
        using var turn = new Turn("/form", HandlerShape.BareCallbackClosesChild);

        await turn.Click();
        await turn.Shows("closed");

        Assert.Equal(turn.Probe.EditorLifetime, turn.Probe.CallbackTokenAfterClose);
        Assert.True(turn.Probe.CallbackTokenAfterClose.IsCancellationRequested);
    }

    [Fact]
    public async Task A_page_left_while_its_load_is_in_flight_trips_no_error_boundary()
    {
        using var turn = new Turn("/list", HandlerShape.GoLast, holdReads: true);
        await turn.Shows("loading");

        turn.Navigate("/form");
        turn.Probe.ReleaseReads();
        await WaitFor.True(() => turn.Probe.ListFault is not null, "the abandoned load to end");

        Assert.IsAssignableFrom<OperationCanceledException>(turn.Probe.ListFault);
        Assert.DoesNotContain("boundary-tripped", turn.Render());
        Assert.Contains("form", turn.Html);
    }

    [Fact]
    public async Task A_provider_fault_from_a_page_that_was_left_trips_no_error_boundary()
    {
        using var turn = new Turn("/list", HandlerShape.GoLast, holdReads: true, wrapCancellation: true);
        await turn.Shows("loading");

        turn.Navigate("/form");
        turn.Probe.ReleaseReads();
        await WaitFor.True(() => turn.Probe.ListFault is not null, "the abandoned load to end");

        Assert.IsType<InvalidOperationException>(turn.Probe.ListFault);
        Assert.DoesNotContain("boundary-tripped", turn.Render());
        Assert.Contains("form", turn.Html);
    }

    [Fact]
    public async Task A_fault_in_a_page_that_is_still_shown_trips_its_error_boundary()
    {
        using var turn = new Turn("/list", HandlerShape.GoLast, holdReads: true, failReads: true);
        await turn.Shows("loading");

        turn.Probe.ReleaseReads();

        await turn.Shows("boundary-tripped");
    }

    public enum HandlerShape
    {
        GoLast,
        GoThenAwait,
        GoFirst,
        Throws,
        OpenPanel,
        CallbackClosesChild,
        BareCallbackClosesChild,
    }

    // What a session is to a handler: the services, the navigator's handler scope, and a render after it.
    private sealed class Turn : IRenderHandle, IDisposable
    {
        private readonly Lock _render = new();
        private readonly ServiceProvider _services;
        private readonly RouteState _route = new();
        private readonly Navigator _navigator;
        private readonly StubComponent _view;

        public Turn(
            string path, HandlerShape shape, bool holdReads = false, bool wrapCancellation = false,
            bool failReads = false)
        {
            Probe = new Probe(shape, holdReads, wrapCancellation, failReads);
            _navigator = new Navigator(_route);
            _services = new ServiceCollection()
                .AddSingleton(_route).AddSingleton(_navigator).AddSingleton(Probe).BuildServiceProvider();
            _view = new StubComponent(() =>
                ErrorBoundary.Fallback((_, _) => Span["boundary-tripped"])[Router.Routes(Routes)])
            {
                RenderHandle = this,
            };
            _route.Path = path;
            Render();
        }

        public Probe Probe { get; }

        public CancellationToken HandlerTimeout { get; init; }

        public string Html { get; private set; } = "";

        public string Render()
        {
            lock (_render)
            {
                return Html = _view.RenderAsLiveRoot(_services);
            }
        }

        public void Navigate(string path)
        {
            _route.Path = path;
            Render();
        }

        public async Task Click()
        {
            using var payload = JsonDocument.Parse("{}");
            using (_navigator.EnterHandler())
            {
                await _view.TryInvokeHandlerAsync(
                    MarkupAssert.Attr(Html, "data-rask-on-click")!, payload.RootElement, _services, HandlerTimeout);
            }

            Render();
        }

        public Task Shows(string text) =>
            WaitFor.True(() => Html.Contains(text, StringComparison.Ordinal), $"'{text}' in: {Html}");

        public Task RequestRender()
        {
            Render();
            return Task.CompletedTask;
        }

        Task IRenderHandle.RenderInScopeAsync() => RequestRender();

        public void Dispose() => _services.Dispose();
    }

    public sealed class Probe(HandlerShape shape, bool holdReads, bool wrapCancellation, bool failReads)
    {
        private readonly TaskCompletionSource _reads = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HandlerShape Shape { get; } = shape;
        public CancellationToken FormLifetime { get; set; }
        public CancellationToken HandlerTokenAfterGo { get; set; }
        public CancellationToken ListLifetime { get; set; }
        public CancellationToken ListOwnTokenInMount { get; set; }
        public CancellationToken ListFirstRead { get; set; }
        public CancellationToken ListSecondRead { get; set; }
        public CancellationToken PanelLifetime { get; set; }
        public CancellationToken PanelRead { get; set; }
        public CancellationToken EditorLifetime { get; set; }
        public CancellationToken CallbackTokenAfterClose { get; set; }
        public Exception? ListFault { get; set; }

        public void ReleaseReads() => _reads.TrySetResult();

        // A read as a data call makes it: no token passed, so it is cancelled with the work in progress — and
        // a provider that wraps the cancellation, as EF's execution strategy does, reports a failure instead.
        public async Task<(int Value, CancellationToken Token)> Read()
        {
            var token = Current.Cancellation;
            if (holdReads)
            {
                await _reads.Task.ConfigureAwait(false);
            }
            else
            {
                await Task.Yield();
            }

            if (failReads)
            {
                throw new InvalidOperationException("the database is down");
            }

            if (token.IsCancellationRequested && wrapCancellation)
            {
                throw new InvalidOperationException(
                    "An exception has been raised that is likely due to a transient failure.",
                    new OperationCanceledException(token));
            }

            token.ThrowIfCancellationRequested();
            return (3, token);
        }

        // A save: its own async method, whose continuation carries the handler's flow onto the pool.
        public static async Task Save() => await Task.Delay(1).ConfigureAwait(false);
    }

    [SkipFactory]
    public sealed class FormPage(Probe probe) : Component
    {
        private bool _panel;
        private bool _closed;

        protected override Component? Render() => probe.Shape switch
        {
            HandlerShape.CallbackClosesChild or HandlerShape.BareCallbackClosesChild when _closed => Span["closed"],
            // As the chain's `.OnSaved(Saved)` stores it: wrapped, which is where its writer is recorded.
            HandlerShape.CallbackClosesChild => new Editor(probe) { OnSaved = new Callback(AutoCallback.Wrap(Saved)!) },
            HandlerShape.BareCallbackClosesChild => new Editor(probe) { OnSaved = new Callback(Saved) },
            _ => Div[Span["form"], Button.OnClick(Submit)["Save"], _panel ? new Panel(probe) : null],
        };

        // The parent's own code, reached through a child's handler: it closes the child, then goes on.
        private async Task Saved()
        {
            probe.FormLifetime = LifetimeTokenInternal;
            _closed = true;
            StateHasChanged();
            await Probe.Save();
            await Probe.Save();
            probe.CallbackTokenAfterClose = Current.Cancellation;
        }

        private async Task Submit()
        {
            probe.FormLifetime = CancellationToken;
            switch (probe.Shape)
            {
                case HandlerShape.GoLast:
                    await Probe.Save();
                    Navigator.RequireCurrent().NavigateTo("/list");
                    break;
                case HandlerShape.GoThenAwait:
                    await Probe.Save();
                    Navigator.RequireCurrent().NavigateTo("/list");
                    await Probe.Save();
                    probe.HandlerTokenAfterGo = Current.Cancellation;
                    break;
                case HandlerShape.GoFirst:
                    Navigator.RequireCurrent().NavigateTo("/list");
                    await Probe.Save();
                    break;
                case HandlerShape.OpenPanel:
                    await Probe.Save();
                    _panel = true;
                    break;
                default:
                    await Probe.Save();
                    throw new InvalidOperationException("save failed");
            }
        }
    }

    [SkipFactory]
    public sealed class ListPage(Probe probe) : Component
    {
        private int? _rows;
        private int? _total;

        protected override async Task OnMount()
        {
            probe.ListLifetime = LifetimeTokenInternal;
            probe.ListOwnTokenInMount = CancellationToken;
            try
            {
                (_rows, probe.ListFirstRead) = await probe.Read();
                (_total, probe.ListSecondRead) = await probe.Read();
            }
            catch (Exception ex)
            {
                probe.ListFault = ex;
                throw;
            }
        }

        protected override Component? Render() =>
            Span[_total is null ? "loading" : $"rows:{_rows} total:{_total}"];
    }

    [SkipFactory]
    public sealed class Editor(Probe probe) : Component
    {
        public Callback OnSaved { get; set; }

        protected override Component? Render() => Button.OnClick(Save)["Save"];

        private async Task Save()
        {
            probe.EditorLifetime = LifetimeTokenInternal;
            await OnSaved.Invoke();
        }
    }

    [SkipFactory]
    public sealed class Panel(Probe probe) : Component
    {
        private bool _loaded;

        protected override async Task OnMount()
        {
            probe.PanelLifetime = LifetimeTokenInternal;
            (_, probe.PanelRead) = await probe.Read();
            _loaded = true;
        }

        protected override Component? Render() => Span[_loaded ? "panel:loaded" : "panel:loading"];
    }
}
