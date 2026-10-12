using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Rask.Core.Diagnostics;
using Rask.Core.Forms;
using Rask.Core.Messaging;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// A save that throws something which names no field — the app's own exception, or a DataAnnotations
// ValidationException out of the store — on a form written the ordinary way, with a fixed list of children.
// The reader is told once: in the form's own messages where it draws them, in a toast where it does not.
// The collection is the one that owns the process-wide diagnostics sink.
[Collection("ConsoleRedirect")]
public partial class FormSubmitFaultTests : global::Rask.Core.RaskMarkup
{
    private const string Sentence = "The name is already in use by another partner.";
    private const string SaveFailed = "The save failed. Please try again.";

    public static TheoryData<Exception> Faults => new()
    {
        new MissingTenantException(),
        new InvalidOperationException("Connection string 'Server=db;Password=hunter2' failed"),
        new AggregateException(new MissingTenantException()),
        new NamedRuleException(),
    };

    public static TheoryData<Exception> Refusals => new()
    {
        new ValidationException(Sentence),
        new AggregateException(new ValidationException(Sentence)),
        new TargetInvocationException(new ValidationException(Sentence)),
    };

    [Theory]
    [MemberData(nameof(Faults))]
    public async Task A_save_that_throws_says_so_in_a_toast_with_no_word_of_the_exception_and_reports_it_to_the_log(Exception thrown)
    {
        var page = new PartnerPage(thrown);
        using var toasts = Toast.Fake();

        var reported = await Reported(page.Submit);

        toasts.Shown(SaveFailed).Once();
        toasts.Shown(SaveFailed).As(ToastLevel.Error);
        Assert.Equal(page.Before, page.Html);
        var fault = Assert.Single(reported, static e => e.Level == RaskLogLevel.Error);
        Assert.Equal("Rask.Forms", fault.Category);
        Assert.Same(thrown, fault.Exception);
        Assert.Equal(1, page.Saves);
    }

    [Fact]
    public async Task A_form_that_draws_its_own_messages_says_the_save_failed_there_and_raises_no_toast()
    {
        var page = new PartnerPage(new MissingTenantException(), summary: true);
        using var toasts = Toast.Fake();

        await page.Submit();

        Assert.Contains($">{SaveFailed}</li>", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant", page.Html, StringComparison.OrdinalIgnoreCase);
        toasts.Shown(SaveFailed).Never();
    }

    [Fact]
    public async Task The_sentence_in_the_forms_own_messages_is_gone_once_the_next_save_goes_through()
    {
        var page = new PartnerPage(new MissingTenantException(), summary: true);
        await page.Submit();
        page.Thrown = null;

        await page.Submit();

        Assert.DoesNotContain(SaveFailed, page.Html, StringComparison.Ordinal);
        Assert.False(page.Form.HasValidationMessages());
    }

    [Fact]
    public async Task A_save_with_no_session_to_show_a_toast_in_is_still_released_and_reported()
    {
        var page = new PartnerPage(new MissingTenantException());

        var reported = await Reported(page.Submit);

        Assert.Equal(page.Before, page.Html);
        Assert.Single(reported, static e => e.Level == RaskLogLevel.Error);
    }

    [Fact]
    public async Task A_save_that_catches_its_own_exception_never_reaches_the_form()
    {
        var page = new PartnerPage(new MissingTenantException(), catches: true);
        using var toasts = Toast.Fake();

        var reported = await Reported(page.Submit);

        toasts.Shown(SaveFailed).Never();
        toasts.Shown("No tenant yet").Once();
        Assert.DoesNotContain(reported, static e => e.Level == RaskLogLevel.Error);
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_validation_sentence_thrown_by_the_save_is_the_toast_when_the_form_has_nowhere_to_draw_it(Exception thrown)
    {
        var page = new PartnerPage(thrown);
        using var toasts = Toast.Fake();

        var reported = await Reported(page.Submit);

        toasts.Shown(Sentence).Once();
        toasts.Shown(Sentence).As(ToastLevel.Error);
        toasts.Shown(SaveFailed).Never();
        Assert.DoesNotContain(reported, static e => e.Level == RaskLogLevel.Error);
        var refused = Assert.Single(reported, static e => e.Level == RaskLogLevel.Information);
        Assert.Contains("was refused: ValidationException", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_validation_sentence_thrown_by_the_save_is_drawn_in_the_forms_own_messages()
    {
        var page = new PartnerPage(new ValidationException(Sentence), summary: true);
        using var toasts = Toast.Fake();

        await page.Submit();

        Assert.Contains($">{Sentence}</li>", page.Html, StringComparison.Ordinal);
        toasts.Shown(Sentence).Never();
    }

    [Fact]
    public async Task A_validation_sentence_that_names_a_bound_field_is_drawn_under_that_field()
    {
        var named = new ValidationException(new ValidationResult(Sentence, [nameof(Partner.Name)]), null, null);
        var page = new PartnerPage(named, summary: true, fieldMessage: true);
        using var toasts = Toast.Fake();

        await page.Submit();

        Assert.Contains($"<p id=\"name-error\">{Sentence}</p>", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<li>", page.Html, StringComparison.Ordinal);
        toasts.Shown(Sentence).Never();
    }

    [Fact]
    public async Task A_refused_save_is_no_error_for_a_form_that_reads_one()
    {
        var page = new PartnerPage(new ValidationException(Sentence), readsError: true);
        using var toasts = Toast.Fake();

        await page.Submit();

        Assert.DoesNotContain("id=\"failed\"", page.Html, StringComparison.Ordinal);
        toasts.Shown(Sentence).Once();
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
    public async Task A_cancelled_save_is_silent_on_the_page_and_in_the_log_as_well()
    {
        var page = new PartnerPage(new OperationCanceledException());
        using var toasts = Toast.Fake();

        var reported = await Reported(page.Submit);

        Assert.Equal(page.Before, page.Html);
        toasts.Shown(SaveFailed).Never();
        Assert.DoesNotContain(reported, static e => e.Level == RaskLogLevel.Error);
    }

    [Fact]
    public async Task A_form_that_reads_the_error_itself_is_handed_the_exception_as_thrown_beside_the_toast()
    {
        var thrown = new MissingTenantException();
        var page = new PartnerPage(thrown, readsError: true);
        using var toasts = Toast.Fake();

        await page.Submit();

        toasts.Shown(SaveFailed).Once();
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

    // The app's own exception that happens to derive from the DataAnnotations one: a fault like any other.
    private sealed class NamedRuleException() : ValidationException("Rule 17 of the tariff table failed");

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
        private readonly bool _fieldMessage;
        private readonly bool _catches;

        internal PartnerPage(
            Exception? thrown, bool summary = false, bool readsError = false, bool fieldMessage = false, bool catches = false)
        {
            Thrown = thrown;
            _summary = summary;
            _readsError = readsError;
            _fieldMessage = fieldMessage;
            _catches = catches;
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
                        _fieldMessage
                            ? Validation.Message.Template(static messages => P.Id("name-error")[messages[0]]).For(() => Model.Name)
                            : null,
                        _summary ? Validation.Summary.Template(entries => Ul[entries.Select(e => (Component)Li.Key(e.Message)[e.Message])]) : null,
                        Button.Type(ButtonType.Submit)["Save"]
                    ]
            ];

        // The app's own handling is a try/catch in its own handler: what it catches the form never sees.
        private async Task Save(Partner partner)
        {
            Saves++;
            try
            {
                await (Thrown is null ? Task.CompletedTask : Task.FromException(Thrown));
            }
            catch (MissingTenantException) when (_catches)
            {
                Toast.Warning("No tenant yet");
            }
        }
    }
}
