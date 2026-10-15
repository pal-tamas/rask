using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Core.Messaging;
using Rask.Wire;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// A handler outside a form — the bin of a list — that throws a real fault, in an app that wrote no error
// boundary around it: the host's own boundary would replace every page for one failed click, so the fault
// is an error toast with the reference the log carries and the page stays as the handler left it. The
// collection owns the process-wide diagnostics sink and the Development switch.
[Collection("ConsoleRedirect")]
public partial class HandlerFaultTests : global::Rask.Core.RaskMarkup
{
    private const string Secret = "Server=db.internal;Password=hunter2";
    private const string Failed = "That did not work. Please try again.";
    private const string Recorded = "The problem has been recorded. If it keeps happening, quote this reference: ";

    [Fact]
    public async Task A_handler_that_throws_under_the_hosts_boundary_says_so_in_an_error_toast_with_the_reference_the_log_carries()
    {
        var thrown = new InvalidOperationException(Secret);
        var page = new TrailerApp(thrown);
        using var toasts = Toast.Fake();

        var reported = await Reported(page.Delete);

        var line = Assert.Single(reported);
        Assert.Equal(RaskLogLevel.Error, line.Level);
        Assert.Equal("Rask.Errors", line.Category);
        Assert.Same(thrown, line.Exception);
        Assert.StartsWith("An unhandled exception reached a toast from an event handler. Reference ", line.Message, StringComparison.Ordinal);
        var said = $"{Failed} {Recorded}{line.Message[^8..]}";
        toasts.Shown(said).Once();
        toasts.Shown(said).As(ToastLevel.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_handler_that_fails_leaves_the_page_as_it_left_it_and_trips_no_boundary(bool awaitsFirst)
    {
        var page = new TrailerApp(new InvalidOperationException(Secret), awaitsFirst);
        using var toasts = Toast.Fake();

        await page.Delete();

        Assert.DoesNotContain("rask-error-boundary", page.Html, StringComparison.Ordinal);
        Assert.Contains("<p id=\"attempts\">1</p>", page.Html, StringComparison.Ordinal);
        Assert.Contains("<li>ABC-123</li>", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_click_after_a_failed_one_is_answered()
    {
        var page = new TrailerApp(new InvalidOperationException(Secret));
        using var toasts = Toast.Fake();
        await page.Delete();
        page.Thrown = null;

        await page.Delete();

        Assert.Contains("<p id=\"attempts\">2</p>", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<li>ABC-123</li>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_toast_reaches_the_outlet_the_sessions_layout_draws_with_no_word_of_the_exception()
    {
        var services = new ServiceCollection().AddSingleton<IToaster>(new Toaster()).BuildServiceProvider();
        var page = new TrailerApp(new InvalidOperationException(Secret), services: services, outlet: true);

        await page.Delete();

        Assert.Contains($"<li data-rask-key=\"0\">Error: {Failed} {Recorded}", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_development_the_toast_names_the_exception_after_its_sentence()
    {
        var page = new TrailerApp(new InvalidOperationException(Secret));
        using var toasts = Toast.Fake();
        var previous = LiveOptions.IsDevelopment;
        LiveOptions.IsDevelopment = true;

        List<RaskDiagnosticEvent> reported;
        try
        {
            reported = await Reported(page.Delete);
        }
        finally
        {
            LiveOptions.IsDevelopment = previous;
        }

        var reference = Assert.Single(reported).Message[^8..];
        toasts.Shown($"{Failed} {Recorded}{reference} InvalidOperationException: {Secret}").Once();
    }

    [Fact]
    public async Task A_production_host_says_nothing_of_the_exception_though_the_process_wide_flag_says_development()
    {
        var services = new ServiceCollection().AddSingleton(HostMode.For(false)).BuildServiceProvider();
        var page = new TrailerApp(new InvalidOperationException(Secret), services: services);
        using var toasts = Toast.Fake();
        var previous = LiveOptions.IsDevelopment;
        LiveOptions.IsDevelopment = true;

        List<RaskDiagnosticEvent> reported;
        try
        {
            reported = await Reported(page.Delete);
        }
        finally
        {
            LiveOptions.IsDevelopment = previous;
        }

        var reference = Assert.Single(reported).Message[^8..];
        toasts.Shown($"{Failed} {Recorded}{reference}").Once();
    }

    [Fact]
    public async Task A_boundary_the_app_wrote_around_the_part_still_takes_the_fault_and_no_toast_is_raised()
    {
        var page = new TrailerApp(new InvalidOperationException(Secret), ownBoundary: true);
        using var toasts = Toast.Fake();

        var reported = await Reported(page.Delete);

        Assert.Contains("<p id=\"own-fallback\">failed</p>", page.Html, StringComparison.Ordinal);
        Assert.Contains("<h1>trailers</h1>", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("rask-error-boundary", page.Html, StringComparison.Ordinal);
        Assert.Contains("an error boundary from an event handler. Reference ", Assert.Single(reported).Message, StringComparison.Ordinal);
        toasts.Shown($"{Failed} {Recorded}{reported[0].Message[^8..]}").Never();
    }

    [Fact]
    public async Task A_handler_the_page_wrote_and_a_child_inside_a_boundary_raised_is_answered_where_it_was_written()
    {
        var page = new TrailerApp(new InvalidOperationException(Secret), binInsideOwnBoundary: true);
        using var toasts = Toast.Fake();

        var reported = await Reported(page.Delete);

        Assert.DoesNotContain("own-fallback", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("rask-error-boundary", page.Html, StringComparison.Ordinal);
        Assert.Contains("a toast from an event handler", Assert.Single(reported).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_render_that_throws_after_a_click_is_still_the_error_page_and_not_a_toast()
    {
        var page = new TrailerApp(thrown: null, rendersBrokenAfterDelete: true);
        using var toasts = Toast.Fake();

        var reported = await Reported(page.Delete);

        Assert.Contains("rask-error-boundary", page.Html, StringComparison.Ordinal);
        Assert.Contains("an error boundary from a render. Reference ", Assert.Single(reported).Message, StringComparison.Ordinal);
        toasts.Shown($"{Failed} {Recorded}{reported[0].Message[^8..]}").Never();
    }

    [Fact]
    public async Task A_session_that_draws_no_toasts_leaves_the_fault_to_the_boundary_and_never_swallows_it()
    {
        var toaster = new Toaster();
        var services = new ServiceCollection().AddSingleton<IToaster>(toaster).BuildServiceProvider();
        var page = new TrailerApp(new InvalidOperationException(Secret), services: services);

        var reported = await Reported(page.Delete);

        Assert.Contains("rask-error-boundary", page.Html, StringComparison.Ordinal);
        Assert.Empty(toaster.Consume());
        Assert.Contains("an error boundary from an event handler. Reference ", Assert.Single(reported).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_under_the_hosts_boundary_is_still_its_own_sentence_with_no_reference()
    {
        const string sentence = "The trailer is on the road.";
        var page = new TrailerApp(new RefusedException(new FieldFailure(sentence, [])));
        using var toasts = Toast.Fake();

        var reported = await Reported(page.Delete);

        toasts.Shown(sentence).Once();
        Assert.Equal(RaskLogLevel.Information, Assert.Single(reported).Level);
    }

    [Fact]
    public async Task A_cancelled_handler_under_the_hosts_boundary_stays_silent_and_goes_to_the_host()
    {
        var page = new TrailerApp(new OperationCanceledException());
        using var toasts = Toast.Fake();

        var reported = await Reported(async () => await Assert.ThrowsAsync<OperationCanceledException>(page.Delete));

        Assert.Empty(reported);
        Assert.DoesNotContain("rask-error-boundary", page.Html, StringComparison.Ordinal);
    }

    private static async Task<List<RaskDiagnosticEvent>> Reported(Func<Task> act)
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Add;
        try
        {
            await act();
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }

        return captured;
    }

    private sealed class RefusedException(params FieldFailure[] failures) : Exception("refused"), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures => failures;
    }

    // An app as a host mounts it — inside the root boundary it did not write — with a list of one trailer
    // and a bin whose delete throws what the test hands it.
    private sealed class TrailerApp
    {
        private readonly RootErrorBoundary _root;
        private readonly IServiceProvider _services;
        private readonly bool _awaitsFirst;
        private readonly bool _rendersBrokenAfterDelete;
        private int _attempts;
        private bool _deleted;

        internal TrailerApp(
            Exception? thrown,
            bool awaitsFirst = false,
            IServiceProvider? services = null,
            bool outlet = false,
            bool ownBoundary = false,
            bool binInsideOwnBoundary = false,
            bool rendersBrokenAfterDelete = false)
        {
            Thrown = thrown;
            _awaitsFirst = awaitsFirst;
            _rendersBrokenAfterDelete = rendersBrokenAfterDelete;
            _services = services ?? RenderHarness.EmptyServices();
            var list = new TrailerList(this, binInsideOwnBoundary);
            _root = new RootErrorBoundary(new StubComponent(() => Div[
                H1["trailers"],
                ownBoundary ? ErrorBoundary.Fallback(static (_, _) => P.Id("own-fallback")["failed"])[list] : list,
                outlet ? Toasts() : null
            ]));
            Html = Render();
        }

        internal Exception? Thrown { get; set; }

        internal string Html { get; private set; }

        internal string Attempts => _attempts.ToString(System.Globalization.CultureInfo.InvariantCulture);

        internal bool Deleted => _deleted;

        internal bool Broken => _rendersBrokenAfterDelete && _deleted;

        internal async Task Delete()
        {
            using var payload = JsonDocument.Parse("{}");

            try
            {
                await _root.TryInvokeHandlerAsync(MarkupAssert.Attr(Html, "data-rask-on-click")!, payload.RootElement, _services);
            }
            finally
            {
                Html = Render();
            }
        }

        internal async Task Remove()
        {
            _attempts++;
            if (_awaitsFirst)
            {
                await Task.Yield();
            }

            if (Thrown is not null)
            {
                throw Thrown;
            }

            _deleted = true;
        }

        private string Render() => _root.RenderAsLiveRoot(_services);

        private static Component Toasts() =>
            ToastOutlet.Template(static (toasts, _) => Ul[toasts.Select(static t => (Component)Li.Key(t.Id)[$"{t.Level}: {t.Message}"])]);
    }

    // The page's own component: it writes the handler, and hands it to a bin that may sit inside a boundary.
    private sealed partial class TrailerList(TrailerApp app, bool binInsideOwnBoundary) : Component
    {
        protected override Component? Render()
        {
            if (app.Broken)
            {
                throw new InvalidOperationException(Secret);
            }

            var bin = new Bin(Delete);
            return Div[
                P.Id("attempts")[app.Attempts],
                Ul[app.Deleted ? null : Li["ABC-123"]],
                binInsideOwnBoundary ? ErrorBoundary.Fallback(static (_, _) => P.Id("own-fallback")["failed"])[bin] : bin
            ];
        }

        private Task Delete() => app.Remove();
    }

    // A child that raises the handler it was handed, as a kit's button raises the page's OnClick.
    private sealed class Bin(Func<Task> onDelete) : Component
    {
        protected override Component? Render() => Button.OnClick(onDelete)["Delete"];
    }
}
