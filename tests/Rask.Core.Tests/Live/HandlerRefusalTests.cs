using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics;
using Rask.Core.Messaging;
using Rask.Wire;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// A handler outside a form — the confirm button of a list's bin — whose own code says no: field failures
// out of the store, or a DataAnnotations ValidationException. The sentence is an error toast; the page is
// not replaced by its error boundary and goes on answering. The collection is the one that owns the
// process-wide diagnostics sink.
[Collection("ConsoleRedirect")]
public partial class HandlerRefusalTests : global::Rask.Core.RaskMarkup
{
    private const string Referenced = "A pótkocsi nem törölhető, mert menetlevél hivatkozik rá.";
    private const string InUse = "The trailer is on the road.";

    public static TheoryData<Exception> Refusals => new()
    {
        new RefusedException(new FieldFailure(Referenced, [], Source: "FK_Waybill_Trailer")),
        new AggregateException(new RefusedException(new FieldFailure(Referenced, [], Source: "FK_Waybill_Trailer"))),
        new TargetInvocationException(new RefusedException(new FieldFailure(Referenced, [], Source: "FK_Waybill_Trailer"))),
        new RefusedException(new FieldFailure(Referenced, ["Plate"], Source: "FK_Waybill_Trailer")),
        new ValidationException(Referenced),
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_handler_that_refuses_says_its_own_sentence_in_an_error_toast_and_is_logged_as_information(Exception thrown)
    {
        var page = new TrailerPage(thrown);
        using var toasts = Toast.Fake();

        var reported = await Reported(page.Delete);

        toasts.Shown(Referenced).Once();
        toasts.Shown(Referenced).As(ToastLevel.Error);
        var entry = Assert.Single(reported);
        Assert.Equal(RaskLogLevel.Information, entry.Level);
        Assert.Equal("Rask.Handler", entry.Category);
        Assert.Contains("was refused: ", entry.Message, StringComparison.Ordinal);
        Assert.Null(entry.Exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_handler_that_refuses_leaves_the_page_inside_its_boundary_with_what_it_changed_before(bool awaitsFirst)
    {
        var page = new TrailerPage(new RefusedException(new FieldFailure(Referenced, [])), awaitsFirst);
        using var toasts = Toast.Fake();

        await page.Delete();

        Assert.DoesNotContain("boundary-fallback", page.Html, StringComparison.Ordinal);
        Assert.Contains("<p id=\"attempts\">1</p>", page.Html, StringComparison.Ordinal);
        Assert.Contains("<li>ABC-123</li>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_click_after_a_refused_one_is_answered()
    {
        var page = new TrailerPage(new RefusedException(new FieldFailure(Referenced, [])));
        using var toasts = Toast.Fake();
        await page.Delete();
        page.Thrown = null;

        await page.Delete();

        Assert.Contains("<p id=\"attempts\">2</p>", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<li>ABC-123</li>", page.Html, StringComparison.Ordinal);
        toasts.Shown(Referenced).Once();
    }

    [Fact]
    public async Task Several_failures_from_a_handler_are_one_toast_with_each_sentence_once_in_order()
    {
        var page = new TrailerPage(new RefusedException(
            new FieldFailure(Referenced, []), new FieldFailure(InUse, ["Plate"]), new FieldFailure(Referenced, [])));
        using var toasts = Toast.Fake();

        await page.Delete();

        toasts.Shown($"{Referenced} {InUse}").Once();
    }

    [Fact]
    public async Task A_refusal_reaches_the_toasts_the_sessions_layout_draws()
    {
        var services = new ServiceCollection().AddSingleton<IToaster>(new Toaster()).BuildServiceProvider();
        var page = new TrailerPage(new RefusedException(new FieldFailure(InUse, [])), services: services, outlet: true);

        await page.Delete();

        Assert.Contains($"<li data-rask-key=\"0\">Error: {InUse}</li>", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("boundary-fallback", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_in_a_session_that_draws_no_toasts_goes_to_the_boundary_like_any_other_exception()
    {
        var toaster = new Toaster();
        var services = new ServiceCollection().AddSingleton<IToaster>(toaster).BuildServiceProvider();
        var page = new TrailerPage(new RefusedException(new FieldFailure(Referenced, [])), services: services);

        await page.Delete();

        Assert.Contains("boundary-fallback", page.Html, StringComparison.Ordinal);
        Assert.Empty(toaster.Consume());
    }

    [Fact]
    public async Task An_exception_that_carries_no_failure_at_all_is_a_fault_and_not_a_refusal()
    {
        var page = new TrailerPage(new RefusedException());
        using var toasts = Toast.Fake();

        await page.Delete();

        Assert.Contains("boundary-fallback", page.Html, StringComparison.Ordinal);
        toasts.Shown("refused").Never();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_handler_that_throws_anything_else_still_trips_its_boundary_and_raises_no_toast(bool subclass)
    {
        Exception thrown = subclass ? new TariffRuleException() : new InvalidOperationException("Server=db;Password=hunter2");
        var page = new TrailerPage(thrown);
        using var toasts = Toast.Fake();

        var reported = await Reported(page.Delete);

        Assert.Contains("<p id=\"boundary-fallback\">", page.Html, StringComparison.Ordinal);
        toasts.Shown(thrown.Message).Never();
        Assert.Empty(reported);
    }

    [Fact]
    public async Task A_cancelled_handler_stays_silent_and_goes_to_the_host_as_before()
    {
        var page = new TrailerPage(new OperationCanceledException());
        using var toasts = Toast.Fake();

        var reported = await Reported(async () => await Assert.ThrowsAsync<OperationCanceledException>(page.Delete));

        Assert.Empty(reported);
        toasts.Shown(new OperationCanceledException().Message).Never();
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

    // The app's own exception that happens to derive from the DataAnnotations one: a fault like any other.
    private sealed class TariffRuleException() : ValidationException("Rule 17 of the tariff table failed");

    // A list of one trailer inside an error boundary, with a bin whose delete throws what the test hands it.
    private sealed class TrailerPage
    {
        private readonly StubComponent _view;
        private readonly Bin _bin;
        private readonly bool _awaitsFirst;
        private readonly bool _outlet;
        private readonly IServiceProvider? _services;
        private int _attempts;
        private bool _deleted;

        internal TrailerPage(Exception? thrown, bool awaitsFirst = false, IServiceProvider? services = null, bool outlet = false)
        {
            Thrown = thrown;
            _awaitsFirst = awaitsFirst;
            _services = services;
            _outlet = outlet;
            _bin = new Bin(this);
            _view = new StubComponent(() => Div[Draw(), _outlet ? Toasts() : null]);
            Html = Render();
        }

        internal Exception? Thrown { get; set; }

        internal string Html { get; private set; }

        internal async Task Delete()
        {
            using var payload = JsonDocument.Parse("{}");

            try
            {
                await _view.TryInvokeHandlerAsync(MarkupAssert.Attr(Html, "data-rask-on-click")!, payload.RootElement, _services);
            }
            finally
            {
                Html = Render();
            }
        }

        private string Render() => _services is null ? _view.RenderAsLiveRoot() : _view.RenderAsLiveRoot(_services);

        private static Component Toasts() =>
            ToastOutlet.Template(static (toasts, _) => Ul[toasts.Select(static t => (Component)Li.Key(t.Id)[$"{t.Level}: {t.Message}"])]);

        private Component Draw() =>
            ErrorBoundary.Fallback(static (_, _) => P.Id("boundary-fallback")["failed"])[
                P.Id("attempts")[_attempts.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                Ul[_deleted ? null : Li["ABC-123"]],
                _bin
            ];

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
    }

    // The bin is a component of its own, as an app's is: its handler's faults go to the boundary around it.
    private sealed class Bin(TrailerPage page) : Component
    {
        protected override Component? Render() => Button.OnClick(Click)["Delete"];

        private Task Click() => page.Remove();
    }
}
