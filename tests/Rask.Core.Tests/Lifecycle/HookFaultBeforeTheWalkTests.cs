using Rask.Core.Diagnostics;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

// OnMount and OnUpdated start before the render walk reaches their component, which is where its boundary
// becomes known. A fault that early used to find no boundary and was only logged, the page left on its
// placeholder. It waits for the walk now, and reaches the boundary a fault of that component's render does.
// The collection owns the process-wide diagnostics sink and the Development switch.
[Collection("ConsoleRedirect")]
public partial class HookFaultBeforeTheWalkTests : global::Rask.Core.RaskMarkup
{
    private const string Secret = "Server=db.internal;Password=hunter2";

    [Theory]
    [InlineData(Hook.Mount)]
    [InlineData(Hook.Updated)]
    public void A_hook_that_throws_before_its_first_await_under_the_hosts_boundary_draws_the_error_page_in_that_pass(Hook hook)
    {
        var root = new RootErrorBoundary(new StubComponent(() => Div[Section[new Loader(hook)]]));

        var html = root.RenderAsLiveRoot(RenderHarness.EmptyServices());

        Assert.True(root.RenderedFallback);
        Assert.Contains("Something went wrong", html, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", html, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reference_the_page_shows_is_logged_once_at_error_as_a_lifecycle_hooks_with_the_exception()
    {
        var root = new RootErrorBoundary(new StubComponent(() => Div[new Loader(Hook.Mount)]));

        var (html, reported) = Reported(() => root.RenderAsLiveRoot(RenderHarness.EmptyServices()));

        var line = Assert.Single(reported);
        Assert.Equal(RaskLogLevel.Error, line.Level);
        Assert.Equal("Rask.Errors", line.Category);
        Assert.StartsWith("An unhandled exception reached an error boundary from a lifecycle hook. Reference ", line.Message, StringComparison.Ordinal);
        Assert.Contains($"quote this reference: {line.Message[^8..]}", html, StringComparison.Ordinal);
        Assert.Equal(Secret, line.Exception!.Message);
    }

    [Fact]
    public void A_boundary_the_app_wrote_around_the_component_takes_the_fault_and_the_rest_of_the_page_stays()
    {
        var root = new RootErrorBoundary(new StubComponent(() => Div[
            P.Id("beside")["beside"],
            ErrorBoundary.Fallback(static (_, _) => P.Id("own")["could not load"])[new Loader(Hook.Mount)]]));

        var html = root.RenderAsLiveRoot(RenderHarness.EmptyServices());

        Assert.False(root.RenderedFallback);
        Assert.Contains("<p id=\"own\">could not load</p>", html, StringComparison.Ordinal);
        Assert.Contains("<p id=\"beside\">beside</p>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hook_whose_await_ends_before_the_walk_reaches_its_component_is_taken_up_by_that_walk()
    {
        var loader = new Loader(Hook.MountAfterAnAwait);
        var root = new RootErrorBoundary(new StubComponent(() => Div[loader]));
        loader.RaiseLifecycleBeforeRender(false);
        await loader.Faulted.Task.WaitAsync(Budget, TestContext.Current.CancellationToken);
        await WaitFor.True(() => HeldHookFault.Holds(loader), Budget, "the fault to wait for the walk");

        var html = root.RenderAsLiveRoot(RenderHarness.EmptyServices());

        Assert.True(root.RenderedFallback);
        Assert.Contains("Something went wrong", html, StringComparison.Ordinal);
        Assert.False(HeldHookFault.Holds(loader));
    }

    [Fact]
    public void In_development_the_page_is_replaced_and_names_the_exception_as_it_does_for_a_render_fault()
    {
        var root = new RootErrorBoundary(new StubComponent(() => Div[new Loader(Hook.Mount)]));
        var previous = LiveOptions.IsDevelopment;
        LiveOptions.IsDevelopment = true;

        string html;
        try
        {
            html = root.RenderAsLiveRoot(RenderHarness.EmptyServices());
        }
        finally
        {
            LiveOptions.IsDevelopment = previous;
        }

        Assert.True(root.RenderedFallback);
        Assert.Contains("Something went wrong", html, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), html, StringComparison.Ordinal);
        Assert.Contains("hunter2", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_component_rendered_with_no_boundary_anywhere_still_logs_the_fault_and_draws_what_it_can()
    {
        var page = new StubComponent(() => Div[new Loader(Hook.Mount)]);

        var (html, reported) = Reported(() => page.RenderAsLiveRoot(RenderHarness.EmptyServices()));

        var line = Assert.Single(reported);
        Assert.Equal(RaskLogLevel.Error, line.Level);
        Assert.Equal("Rask.Lifecycle", line.Category);
        Assert.Equal("Rask lifecycle hook on Loader faulted", line.Message);
        Assert.Contains("loading", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_component_that_leaves_before_any_walk_reaches_it_has_its_fault_recorded_and_not_shown()
    {
        var loader = new Loader(Hook.Mount);

        var (_, reported) = Reported(() =>
        {
            using (LiveRenderContext.Begin(loader, RenderHarness.EmptyServices()))
            {
                loader.RaiseLifecycleBeforeRender(false);
                _ = loader.RaiseUnmount();
            }

            return string.Empty;
        });

        var line = Assert.Single(reported);
        Assert.Equal(RaskLogLevel.Information, line.Level);
        Assert.Equal("Rask lifecycle hook on Loader faulted after it left the page", line.Message);
        Assert.False(HeldHookFault.Holds(loader));
    }

    [Fact]
    public void A_root_rendered_with_no_host_whose_own_hook_faults_still_logs_it()
    {
        var root = new Loader(Hook.Mount);

        var (html, reported) = Reported(() => root.RenderAsLiveRoot(RenderHarness.EmptyServices()));

        var line = Assert.Single(reported);
        Assert.Equal(RaskLogLevel.Error, line.Level);
        Assert.Equal("Rask lifecycle hook on Loader faulted", line.Message);
        Assert.Contains("loading", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovering_the_boundary_runs_the_load_that_faulted_again_though_nothing_above_it_renders_anew()
    {
        var loader = new Loader(Hook.Mount);
        var boundary = new ErrorBoundary();
        boundary.SetProps([Section[loader]], static (_, _) => P.Id("own")["could not load"]);
        var root = new RootErrorBoundary(new StubComponent(() => Div[boundary]));
        var services = RenderHarness.EmptyServices();
        var faulted = root.RenderAsLiveRoot(services);
        loader.Mended = true;

        boundary.Recover();
        var recovered = root.RenderAsLiveRoot(services);

        Assert.Contains("could not load", faulted, StringComparison.Ordinal);
        Assert.Contains("<p>the row</p>", recovered, StringComparison.Ordinal);
        Assert.Equal(2, loader.Mounts);
    }

    [Fact]
    public void A_boundary_still_showing_its_fallback_does_not_run_the_load_again()
    {
        var loader = new Loader(Hook.Mount);
        var boundary = new ErrorBoundary();
        boundary.SetProps([loader], static (_, _) => P.Id("own")["could not load"]);
        var root = new RootErrorBoundary(new StubComponent(() => Div[boundary]));
        var services = RenderHarness.EmptyServices();
        _ = root.RenderAsLiveRoot(services);

        var again = root.RenderAsLiveRoot(services);

        Assert.Contains("could not load", again, StringComparison.Ordinal);
        Assert.Equal(1, loader.Mounts);
    }

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static (string Html, List<RaskDiagnosticEvent> Reported) Reported(Func<string> render)
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        // The sink is the whole process's: a class running beside this one reports into it too. Only what was
        // said about this class's own fault is counted.
        RaskDiagnostics.Sink = line =>
        {
            if (line.Exception is not { Message: Secret })
            {
                return;
            }

            lock (captured)
            {
                captured.Add(line);
            }
        };

        try
        {
            return (render(), captured);
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }

    public enum Hook
    {
        Mount,
        Updated,
        MountAfterAnAwait,
    }

    private sealed class Loader(Hook hook) : Component
    {
        private string? _row;

        public TaskCompletionSource Faulted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>How many times <c>OnMount</c> has run.</summary>
        public int Mounts { get; private set; }

        /// <summary>Whether what was wrong has been put right: a read answers from then on.</summary>
        public bool Mended { get; set; }

        protected override async Task OnMount()
        {
            Mounts++;
            if (hook == Hook.MountAfterAnAwait)
            {
                await Task.Yield();
                Faulted.TrySetResult();
            }

            if (hook != Hook.Updated)
            {
                _row = await Read();
            }
        }

        protected override async Task OnUpdated()
        {
            if (hook == Hook.Updated)
            {
                _row = await Read();
            }
        }

        protected override Component? Render() => P[_row ?? "loading"];

        private Task<string> Read() =>
            Mended ? Task.FromResult("the row") : Task.FromException<string>(new InvalidOperationException(Secret));
    }
}
