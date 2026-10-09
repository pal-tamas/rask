using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Diagnostics;
using Rask.Core.Forms;
using Rask.Wire;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// A submit handler that throws field failures — what the database says about a duplicate — leaves each
// message under the field it names. The collection is the one that owns the process-wide diagnostics sink.
[Collection("ConsoleRedirect")]
public partial class FormFieldFailureTests : global::Rask.Core.RaskMarkup
{
    private const string Taken = "That invoice number is taken.";
    private const string Booked = "This driver is already booked then.";

    [Fact]
    public async Task A_failure_naming_one_field_shows_its_message_under_that_field()
    {
        var page = new InvoicePage(new Refused(new FieldFailure(Taken, ["Number"])));

        await page.Submit();

        Assert.Equal([Taken], page.Messages(page.Model, "Number"));
        Assert.Empty(page.Messages(page.Model, "Year"));
        Assert.Contains($"<p class=\"number\">{Taken}</p>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failure_under_a_field_is_neither_the_forms_error_nor_a_submit_still_running()
    {
        var page = new InvoicePage(new Refused(new FieldFailure(Taken, ["Number"])));

        await page.Submit();

        Assert.Null(page.Errors[^1]);
        Assert.False(page.Submitting[^1]);
    }

    [Fact]
    public async Task A_failure_naming_two_fields_shows_its_message_under_both()
    {
        var page = new InvoicePage(new Refused(new FieldFailure(Taken, ["Year", "Number"])));

        await page.Submit();

        Assert.Equal([Taken], page.Messages(page.Model, "Year"));
        Assert.Equal([Taken], page.Messages(page.Model, "Number"));
    }

    [Fact]
    public async Task A_range_failure_is_told_under_one_field_and_only_marks_its_bounds()
    {
        var page = new InvoicePage(new Refused(new FieldFailure(Booked, ["Driver"], ["From", "To"])));

        await page.Submit();

        Assert.Equal([Booked], page.Messages(page.Model, "Driver"));
        Assert.Empty(page.Messages(page.Model, "From"));
        Assert.Empty(page.Messages(page.Model, "To"));
        Assert.True(page.Form.IsInvalid(new FieldIdentifier(page.Model, "From")));
        Assert.True(page.Form.IsInvalid(new FieldIdentifier(page.Model, "To")));
        Assert.False(page.Form.IsInvalid(new FieldIdentifier(page.Model, "Number")));
    }

    [Fact]
    public async Task A_failure_on_a_nested_path_lands_on_the_nested_objects_field()
    {
        var page = new InvoicePage(new Refused(new FieldFailure("Too much.", ["Price.Amount"])));

        await page.Submit();

        Assert.Equal(["Too much."], page.Messages(page.Model.Price, "Amount"));
    }

    [Fact]
    public async Task A_failure_on_a_row_of_a_collection_lands_on_that_rows_field()
    {
        var page = new InvoicePage(new Refused(new FieldFailure(Booked, ["Lines[2].From"], ["Lines[2].To"])));

        await page.Submit();

        Assert.Equal([Booked], page.Messages(page.Model.Lines[2], "From"));
        Assert.Empty(page.Messages(page.Model.Lines[1], "From"));
        Assert.True(page.Form.IsInvalid(new FieldIdentifier(page.Model.Lines[2], "To")));
    }

    [Fact]
    public async Task A_failure_over_several_rows_is_told_under_each_rows_field()
    {
        var page = new InvoicePage(new Refused(new FieldFailure(Booked, ["Lines[0].From", "Lines[2].From"])));

        await page.Submit();

        Assert.Equal([Booked], page.Messages(page.Model.Lines[0], "From"));
        Assert.Equal([Booked], page.Messages(page.Model.Lines[2], "From"));
    }

    [Fact]
    public async Task A_failure_naming_no_field_on_the_form_goes_to_its_summary()
    {
        var page = new InvoicePage(new Refused(new FieldFailure("Closed for the year.", ["Period"])), summary: true);

        await page.Submit();

        Assert.Contains(">Closed for the year.</li>", page.Html, StringComparison.Ordinal);
        Assert.Null(page.Errors[^1]);
    }

    [Fact]
    public async Task A_failure_about_the_whole_submission_goes_to_the_forms_summary()
    {
        var page = new InvoicePage(new Refused(new FieldFailure("Closed for the year.", [])), summary: true);

        await page.Submit();

        Assert.Contains(">Closed for the year.</li>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failure_nothing_on_the_page_would_show_stays_the_forms_error()
    {
        var refused = new Refused(new FieldFailure("Closed for the year.", ["Period"]));
        var page = new InvoicePage(refused);

        var reported = await Reported(page.Submit);

        Assert.Same(refused, page.Errors[^1]);
        Assert.Contains(reported, e => e.Level == RaskLogLevel.Error);
    }

    [Fact]
    public async Task A_failure_inside_an_aggregate_is_still_shown_under_its_field()
    {
        var page = new InvoicePage(new AggregateException(new Refused(new FieldFailure(Taken, ["Number"]))));

        await page.Submit();

        Assert.Equal([Taken], page.Messages(page.Model, "Number"));
        Assert.Null(page.Errors[^1]);
    }

    [Fact]
    public async Task A_failure_thrown_through_a_reflected_call_is_still_shown_under_its_field()
    {
        var page = new InvoicePage(
            new TargetInvocationException(new Refused(new FieldFailure(Taken, ["Number"]))));

        await page.Submit();

        Assert.Equal([Taken], page.Messages(page.Model, "Number"));
    }

    [Fact]
    public async Task An_exception_that_names_no_fields_is_still_the_forms_error_and_a_fault()
    {
        var boom = new InvalidOperationException("boom");
        var page = new InvoicePage(boom);

        var reported = await Reported(page.Submit);

        Assert.Same(boom, page.Errors[^1]);
        Assert.Contains(reported, e => e.Level == RaskLogLevel.Error && ReferenceEquals(e.Exception, boom));
    }

    [Fact]
    public async Task Editing_one_field_of_a_failure_clears_it_from_every_field_it_names()
    {
        var page = new InvoicePage(new Refused(new FieldFailure(Taken, ["Year", "Number"])));
        await page.Submit();

        await page.Type("number", "43");

        Assert.Empty(page.Messages(page.Model, "Number"));
        Assert.Empty(page.Messages(page.Model, "Year"));
        Assert.DoesNotContain(Taken, page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Editing_a_marked_field_clears_the_message_it_was_marked_for()
    {
        var page = new InvoicePage(new Refused(new FieldFailure(Booked, ["Driver"], ["From", "To"])));
        await page.Submit();

        await page.Type("to", "2026-11-02");

        Assert.Empty(page.Messages(page.Model, "Driver"));
        Assert.False(page.Form.IsInvalid(new FieldIdentifier(page.Model, "From")));
    }

    [Fact]
    public async Task Editing_a_field_the_failure_does_not_name_leaves_it_in_place()
    {
        var page = new InvoicePage(new Refused(new FieldFailure(Taken, ["Year", "Number"])));
        await page.Submit();

        await page.Type("driver", "Bea");

        Assert.Equal([Taken], page.Messages(page.Model, "Number"));
    }

    [Fact]
    public async Task The_next_submit_validates_afresh_and_goes_through()
    {
        var page = new InvoicePage(new Refused(new FieldFailure(Taken, ["Year", "Number"])));
        await page.Submit();

        page.Thrown = null;
        await page.Submit();

        Assert.Equal(2, page.Saves);
        Assert.False(page.Form.HasValidationMessages());
        Assert.False(page.Form.IsInvalid(new FieldIdentifier(page.Model, "Year")));
    }

    [Fact]
    public async Task A_failure_shown_under_its_field_is_logged_as_information_with_its_source()
    {
        var page = new InvoicePage(new Refused(new FieldFailure(Taken, ["Year", "Number"], Source: "IX_Invoice_Number")));

        var reported = await Reported(page.Submit);

        var entry = Assert.Single(reported, e => string.Equals(e.Category, "Rask.Forms", StringComparison.Ordinal));
        Assert.Equal(RaskLogLevel.Information, entry.Level);
        Assert.Contains("IX_Invoice_Number", entry.Message, StringComparison.Ordinal);
        Assert.Null(entry.Exception);
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

    private sealed class Refused(params FieldFailure[] failures) : Exception("refused"), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures => failures;
    }

    private sealed class Invoice
    {
        public string Year { get; set; } = "2026";
        public string Number { get; set; } = "42";
        public string Driver { get; set; } = "Ada";
        public string From { get; set; } = "2026-10-01";
        public string To { get; set; } = "2026-10-31";
        public Money Price { get; set; } = new();
        public List<Line> Lines { get; set; } = [new(), new(), new()];
    }

    private sealed class Money
    {
        public string Amount { get; set; } = "10";
    }

    private sealed class Line
    {
        public string From { get; set; } = "2026-10-01";
        public string To { get; set; } = "2026-10-31";
    }

    // One form over an invoice, saved by a handler that throws what the test hands it.
    private sealed partial class InvoicePage
    {
        private readonly StubComponent _view;

        internal InvoicePage(Exception? thrown, bool summary = false)
        {
            Summary = summary;
            Thrown = thrown;
            Form = new EditContext(Model);
            _view = new StubComponent(Draw);
            Html = _view.RenderAsLiveRoot();
        }

        internal Invoice Model { get; } = new();

        internal EditContext Form { get; }

        internal Exception? Thrown { get; set; }

        private bool Summary { get; }

        internal int Saves { get; private set; }

        internal List<Exception?> Errors { get; } = [];

        internal List<bool> Submitting { get; } = [];

        internal string Html { get; private set; }

        internal IReadOnlyList<string> Messages(object owner, string name) =>
            Form.GetValidationMessages(new FieldIdentifier(owner, name));

        internal async Task Submit()
        {
            using var payload = JsonDocument.Parse("{\"form\":{}}");

            await _view.TryInvokeHandlerAsync(MarkupAssert.Attr(Html, "data-rask-on-submit")!, payload.RootElement);

            Html = _view.RenderAsLiveRoot();
        }

        internal async Task Type(string id, string value)
        {
            var handler = HandlerOf().Match(Html.Substring(Html.IndexOf($"<input id=\"{id}\"", StringComparison.Ordinal)));
            using var payload = JsonDocument.Parse(JsonSerializer.Serialize(new { value }));

            await _view.TryInvokeHandlerAsync(handler.Groups[1].Value, payload.RootElement);

            Html = _view.RenderAsLiveRoot();
        }

        private Component Draw() =>
            Markup.Form.Model(Model).Context(Form).OnSubmit(Save)[f =>
            {
                Errors.Add(f.Error);
                Submitting.Add(f.Submitting);
                return
                [
                    Input.Bind(() => Model.Year).Id("year"),
                    Input.Bind(() => Model.Number).Id("number"),
                    Validation.Message.Template(messages => P.Class("number")[messages[0]]).For(() => Model.Number),
                    Input.Bind(() => Model.Driver).Id("driver"),
                    Input.Bind(() => Model.From).Id("from"),
                    Input.Bind(() => Model.To).Id("to"),
                    Input.Bind(() => Model.Price.Amount).Id("amount"),
                    Div[Model.Lines.Select((line, i) => (Component)Div.Key(i)[Input.Bind(() => line.From), Input.Bind(() => line.To)])],
                    Summary ? Validation.Summary.Template(entries => Ul[entries.Select(e => (Component)Li.Key(e.Message)[e.Message])]) : null,
                ];
            }
            ];

        private Task Save(Invoice invoice)
        {
            Saves++;
            return Thrown is null ? Task.CompletedTask : Task.FromException(Thrown);
        }

        [GeneratedRegex("data-rask-on-input=\"([^\"]+)\"")]
        private static partial Regex HandlerOf();
    }
}
