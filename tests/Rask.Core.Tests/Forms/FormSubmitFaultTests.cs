using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Rask.Core.Diagnostics;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// A save that throws something which names no field — the app's own exception, or a DataAnnotations
// ValidationException out of the store — on a form written the ordinary way, with a fixed list of children.
// Pinned as it is TODAY: the reader is told nothing. The tests named "today" flip when the form says so itself.
// The collection is the one that owns the process-wide diagnostics sink.
[Collection("ConsoleRedirect")]
public partial class FormSubmitFaultTests : global::Rask.Core.RaskMarkup
{
    private const string Sentence = "The name is already in use by another partner.";

    public static TheoryData<Exception> Faults => new()
    {
        new MissingTenantException(),
        new ValidationException(Sentence),
        new InvalidOperationException("Connection string 'Server=db;Password=hunter2' failed"),
    };

    [Theory]
    [MemberData(nameof(Faults))]
    public async Task Today_a_save_that_throws_leaves_the_page_exactly_as_it_was_and_says_so_only_in_the_log(Exception thrown)
    {
        var page = new PartnerPage(thrown);
        using var toasts = Toast.Fake();

        var reported = await Reported(page.Submit);

        // Wanted: the reader is told the save failed. Today the markup after is the markup before.
        Assert.Equal(page.Before, page.Html);
        toasts.Shown(Sentence).Never();
        var fault = Assert.Single(reported, static e => e.Level == RaskLogLevel.Error);
        Assert.Equal("Rask.Forms", fault.Category);
        Assert.Same(thrown, fault.Exception);
        Assert.Equal(1, page.Saves);
    }

    [Fact]
    public async Task Today_a_validation_sentence_thrown_by_the_save_reaches_neither_the_summary_nor_a_field()
    {
        var page = new PartnerPage(new ValidationException(Sentence), summary: true);

        await page.Submit();

        // Wanted (a decision): its message where the form's own messages are drawn. Today it is a fault.
        Assert.DoesNotContain(Sentence, page.Html, StringComparison.Ordinal);
        Assert.False(page.Form.HasValidationMessages());
    }

    [Fact]
    public async Task A_save_that_throws_does_not_trip_the_error_boundary_around_the_form()
    {
        var page = new PartnerPage(new MissingTenantException());

        await page.Submit();

        Assert.Contains("<form", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("boundary-fallback", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_save_that_throws_releases_the_submit_and_the_next_one_runs()
    {
        var page = new PartnerPage(new MissingTenantException());
        await page.Submit();
        page.Thrown = null;

        await page.Submit();

        Assert.Equal(2, page.Saves);
        Assert.DoesNotContain("disabled", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancelled_save_is_silent_in_the_log_as_well()
    {
        var page = new PartnerPage(new OperationCanceledException());

        var reported = await Reported(page.Submit);

        Assert.Equal(page.Before, page.Html);
        Assert.DoesNotContain(reported, static e => e.Level == RaskLogLevel.Error);
    }

    [Fact]
    public async Task A_form_that_reads_the_error_itself_is_handed_the_exception_as_thrown()
    {
        var thrown = new MissingTenantException();
        var page = new PartnerPage(thrown, readsError: true);

        await page.Submit();

        // The page decides what to say. Whatever the default becomes, this stays the way to say it yourself.
        Assert.Contains("<p id=\"failed\">MissingTenantException</p>", page.Html, StringComparison.Ordinal);
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

    private sealed class MissingTenantException() : Exception("No tenant is selected for partner 4114");

    private sealed class Partner
    {
        public string Name { get; set; } = "Acme";
    }

    // One form over a partner inside an error boundary, saved by a handler that throws what the test hands it.
    private sealed class PartnerPage
    {
        private readonly StubComponent _view;
        private readonly bool _summary;
        private readonly bool _readsError;

        internal PartnerPage(Exception? thrown, bool summary = false, bool readsError = false)
        {
            Thrown = thrown;
            _summary = summary;
            _readsError = readsError;
            Form = new EditContext(Model);
            _view = new StubComponent(Draw);
            Html = Before = _view.RenderAsLiveRoot();
        }

        internal Partner Model { get; } = new();

        internal EditContext Form { get; }

        internal Exception? Thrown { get; set; }

        internal int Saves { get; private set; }

        internal string Before { get; }

        internal string Html { get; private set; }

        internal async Task Submit()
        {
            using var payload = JsonDocument.Parse("{\"form\":{}}");

            await _view.TryInvokeHandlerAsync(MarkupAssert.Attr(Html, "data-rask-on-submit")!, payload.RootElement);

            Html = _view.RenderAsLiveRoot();
        }

        private Component Draw() =>
            ErrorBoundary.Fallback(static (_, _) => P.Id("boundary-fallback")["failed"])[
                _readsError
                    ? Markup.Form.Model(Model).Context(Form).OnSubmit(Save)[f =>
                    [
                        Input.Bind(() => Model.Name).Id("name"),
                        f.Error is { } error ? P.Id("failed")[error.GetType().Name] : null,
                        Button.Type(ButtonType.Submit).Disabled(f.Submitting)["Save"],
                    ]]
                    : Markup.Form.Model(Model).Context(Form).OnSubmit(Save)[
                        Input.Bind(() => Model.Name).Id("name"),
                        _summary ? Validation.Summary.Template(entries => Ul[entries.Select(e => (Component)Li.Key(e.Message)[e.Message])]) : null,
                        Button.Type(ButtonType.Submit)["Save"]
                    ]
            ];

        private Task Save(Partner partner)
        {
            Saves++;
            return Thrown is null ? Task.CompletedTask : Task.FromException(Thrown);
        }
    }
}
