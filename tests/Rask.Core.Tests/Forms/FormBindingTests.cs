using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

public partial class FormBindingTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_bound_input_renders_its_value_from_the_getter_and_names_its_field()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var page = Test.Render(() => Form.Model(p)[
            Input.Bind(() => p.Name),
            Input.Bind(() => p.Age),
            Input.Bind(() => p.Subscribed)
        ]);

        var html = page.Html;

        Assert.Contains("name=\"Name\"", html);
        Assert.Contains("value=\"Ada\"", html);
        Assert.Contains("type=\"number\"", html);
        Assert.Contains("name=\"Age\"", html);
        Assert.Contains("value=\"30\"", html);
        Assert.Contains("type=\"checkbox\"", html);
    }

    [Fact]
    public async Task An_input_event_updates_a_bound_string_field()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var page = Test.Render(() => Form.Model(p)[
            Input.Bind(() => p.Name)
        ]);

        var inputId = page.HandlerId("input");
        Assert.NotNull(inputId);

        var ok = await page.TryInvokeAsync(inputId!, "{\"value\":\"Bea\"}");

        Assert.True(ok);
        Assert.Equal("Bea", p.Name);
    }

    [Fact]
    public async Task A_change_event_updates_a_bound_numeric_field_and_marks_it_touched()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var page = Test.Render(() => Form.Model(p)[
            Input.Bind(() => p.Age)
        ]);

        var changeId = page.HandlerId("change");
        Assert.NotNull(changeId);

        var ok = await page.TryInvokeAsync(changeId!, "{\"value\":\"42\"}");

        Assert.True(ok);
        Assert.Equal(42, p.Age);
    }

    [Fact]
    public async Task Submitting_an_invalid_model_calls_OnInvalidSubmit_not_OnValidSubmit()
    {
        var p = new Person { Name = "", Age = 0 };
        var validCalled = 0;
        var invalidCalled = 0;

        var page = Test.Render(() => Form.Model(p)
            .OnValidSubmit(_ => validCalled++)
            .OnInvalidSubmit(_ => invalidCalled++)
            .Validate(m =>
                string.IsNullOrEmpty(m.Name) ? new[] { "Name required" } : Array.Empty<string>())[
            Input.Bind(() => p.Name), Input.Bind(() => p.Age)
        ]);

        await page.SubmitAsync("{\"form\":{\"Name\":\"\",\"Age\":\"0\"}}");

        Assert.Equal(0, validCalled);
        Assert.Equal(1, invalidCalled);
    }

    [Fact]
    public async Task The_async_form_Validate_overload_adds_a_form_level_message()
    {
        // Drives Form<TModel>'s async Validate overload: the lambda binds to
        // Func<TModel, CancellationToken, ValueTask<IEnumerable<string>>> with no cast,
        // and the async messages attach to FieldIdentifier(Model, "") — exactly where
        // ValidationSummary / form-scoped readers look.
        var p = new Person { Name = "Ada", Age = 30 };
        EditContext? captured = null;

        var page = Test.Render(() => Form.Model(p).Validate(async (m, ct) =>
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                return string.IsNullOrEmpty(m.Name)
                    ? new[] { "async-form-rule" }
                    : Array.Empty<string>();
            })[
            Input.Bind(() => p.Name),
            Test.EditContextProbe(c => captured = c)
        ]);

        Assert.NotNull(captured);

        // Force the name to be blank so the async rule produces a message.
        p.Name = "";
        await captured!.ValidateAsync();

        Assert.Contains("async-form-rule",
            captured.GetValidationMessages(new FieldIdentifier(p, string.Empty)));
    }

    [Fact]
    public async Task Submitting_a_valid_model_calls_OnValidSubmit_with_the_populated_model()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        Person? captured = null;

        var page = Test.Render(() => Form.Model(p).OnValidSubmit(m => captured = m)[Input.Bind(() => p.Name), Input.Bind(() => p.Age)]);

        await page.SubmitAsync("{\"form\":{\"Name\":\"Ada\",\"Age\":\"30\"}}");

        Assert.Same(p, captured);
        Assert.Equal("Ada", captured!.Name);
    }

    [Fact]
    public void The_EditContext_persists_across_renders_for_the_same_model()
    {
        var p = new Person { Name = "", Age = 30 };
        var captures = new List<EditContext>();

        // Render() renders once itself, so one more call is the second frame.
        var page = Test.Render(() => Form.Model(p)[
            Test.EditContextProbe(captures.Add)
        ]);

        page.Render();

        Assert.Equal(2, captures.Count);
        Assert.Same(captures[0], captures[1]);
    }

    [Fact]
    public void A_null_nullable_int_renders_an_empty_value()
    {
        var p = new Person { Name = "Ada", Age = 30, OptionalAge = null };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.OptionalAge)]);

        var html = page.Html;

        Assert.Contains("name=\"OptionalAge\"", html);
        Assert.Contains("type=\"number\"", html);
        Assert.Contains("value=\"\"", html);
    }

    [Fact]
    public void A_set_nullable_int_renders_its_formatted_value()
    {
        var p = new Person { Name = "Ada", Age = 30, OptionalAge = 7 };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.OptionalAge)]);

        var html = page.Html;

        Assert.Contains("value=\"7\"", html);
    }

    [Fact]
    public void A_nullable_decimal_formats_with_the_invariant_culture()
    {
        var p = new Person { Name = "Ada", Age = 30, Price = 19.95m };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.Price)]);

        var html = page.Html;

        Assert.Contains("type=\"number\"", html);
        Assert.Contains("value=\"19.95\"", html);
    }

    [Fact]
    public void A_set_nullable_DateTime_renders_in_ISO_format()
    {
        var p = new Person { Name = "Ada", Age = 30, StartedAt = new DateTime(2025, 5, 14, 9, 30, 0) };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.StartedAt)]);

        var html = page.Html;

        Assert.Contains("type=\"datetime-local\"", html);
        Assert.Contains("value=\"2025-05-14T09:30\"", html);
    }

    [Fact]
    public void A_set_nullable_DateOnly_renders_an_ISO_date()
    {
        var p = new Person { Name = "Ada", Age = 30, Birthday = new DateOnly(1990, 1, 2) };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.Birthday)]);

        var html = page.Html;

        Assert.Contains("type=\"date\"", html);
        Assert.Contains("value=\"1990-01-02\"", html);
    }

    [Fact]
    public async Task An_empty_change_sets_a_nullable_int_to_null()
    {
        var p = new Person { Name = "Ada", Age = 30, OptionalAge = 7 };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.OptionalAge)]);

        var changeId = page.HandlerId("change");
        Assert.NotNull(changeId);

        var ok = await page.TryInvokeAsync(changeId!, "{\"value\":\"\"}");

        Assert.True(ok);
        Assert.Null(p.OptionalAge);
    }

    [Fact]
    public async Task A_valid_change_sets_a_nullable_int_to_the_typed_value()
    {
        var p = new Person { Name = "Ada", Age = 30, OptionalAge = null };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.OptionalAge)]);

        var ok = await page.TryInvokeAsync(page.HandlerId("change")!, "{\"value\":\"42\"}");

        Assert.True(ok);
        Assert.Equal(42, p.OptionalAge);
    }

    [Fact]
    public async Task An_invalid_change_leaves_a_nullable_int_unchanged()
    {
        var p = new Person { Name = "Ada", Age = 30, OptionalAge = 7 };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.OptionalAge)]);

        var ok = await page.TryInvokeAsync(page.HandlerId("change")!, "{\"value\":\"not-a-number\"}");

        // Action still completes (TouchAndValidateHandler always runs validation after the
        // optional set), but the property retains its prior value because TrySetTyped failed.
        Assert.True(ok);
        Assert.Equal(7, p.OptionalAge);
    }

    [Fact]
    public async Task An_empty_change_sets_a_nullable_decimal_to_null()
    {
        var p = new Person { Name = "Ada", Age = 30, Price = 19.95m };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.Price)]);

        var ok = await page.TryInvokeAsync(page.HandlerId("change")!, "{\"value\":\"\"}");

        Assert.True(ok);
        Assert.Null(p.Price);
    }

    [Fact]
    public async Task An_empty_change_sets_a_nullable_DateTime_to_null()
    {
        var p = new Person { Name = "Ada", Age = 30, StartedAt = new DateTime(2025, 5, 14, 9, 30, 0) };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.StartedAt)]);

        var ok = await page.TryInvokeAsync(page.HandlerId("change")!, "{\"value\":\"\"}");

        Assert.True(ok);
        Assert.Null(p.StartedAt);
    }

    [Fact]
    public async Task An_empty_change_sets_a_nullable_DateOnly_to_null()
    {
        var p = new Person { Name = "Ada", Age = 30, Birthday = new DateOnly(1990, 1, 2) };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.Birthday)]);

        var ok = await page.TryInvokeAsync(page.HandlerId("change")!, "{\"value\":\"\"}");

        Assert.True(ok);
        Assert.Null(p.Birthday);
    }

    [Fact]
    public async Task A_valid_change_sets_a_nullable_decimal_to_the_typed_value()
    {
        var p = new Person { Name = "Ada", Age = 30, Price = null };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.Price)]);

        var ok = await page.TryInvokeAsync(page.HandlerId("change")!, "{\"value\":\"12.5\"}");

        Assert.True(ok);
        Assert.Equal(12.5m, p.Price);
    }

    [Fact]
    public async Task A_valid_ISO_change_sets_a_nullable_DateTime_to_the_typed_value()
    {
        var p = new Person { Name = "Ada", Age = 30, StartedAt = null };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.StartedAt)]);

        var ok = await page.TryInvokeAsync(page.HandlerId("change")!, "{\"value\":\"2025-05-14T09:30\"}");

        Assert.True(ok);
        Assert.Equal(new DateTime(2025, 5, 14, 9, 30, 0), p.StartedAt);
    }

    [Fact]
    public async Task A_valid_ISO_change_sets_a_nullable_DateOnly_to_the_typed_value()
    {
        var p = new Person { Name = "Ada", Age = 30, Birthday = null };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.Birthday)]);

        var ok = await page.TryInvokeAsync(page.HandlerId("change")!, "{\"value\":\"1990-01-02\"}");

        Assert.True(ok);
        Assert.Equal(new DateOnly(1990, 1, 2), p.Birthday);
    }

    [Fact]
    public async Task A_valid_change_parses_a_nullable_enum()
    {
        var p = new Person { Name = "Ada", Age = 30, Status = null };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.Status)]);

        var ok = await page.TryInvokeAsync(page.HandlerId("change")!, "{\"value\":\"Active\"}");

        Assert.True(ok);
        Assert.Equal(PersonStatus.Active, p.Status);
    }

    [Fact]
    public async Task An_empty_change_sets_a_nullable_enum_to_null()
    {
        var p = new Person { Name = "Ada", Age = 30, Status = PersonStatus.Active };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.Status)]);

        var ok = await page.TryInvokeAsync(page.HandlerId("change")!, "{\"value\":\"\"}");

        Assert.True(ok);
        Assert.Null(p.Status);
    }

    [Fact]
    public async Task An_empty_change_sets_a_non_nullable_int_to_default()
    {
        // Non-nullable value-type bindings (here `int Age`) treat empty input as `default(T)`
        // so the user can clear a number/date/enum input. The sibling nullable test above
        // (An_empty_change_sets_a_nullable_int_to_null) pins the null path for `int?`.
        var p = new Person { Name = "Ada", Age = 30 };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.Age)]);

        var ok = await page.TryInvokeAsync(page.HandlerId("change")!, "{\"value\":\"\"}");

        Assert.True(ok);
        Assert.Equal(0, p.Age);
    }

    [Fact]
    public async Task An_empty_change_sets_a_non_nullable_decimal_to_default()
    {
        var p = new Person { Name = "Ada", Age = 30, Salary = 5000m };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.Salary)]);

        var ok = await page.TryInvokeAsync(page.HandlerId("change")!, "{\"value\":\"\"}");

        Assert.True(ok);
        Assert.Equal(0m, p.Salary);
    }

    [Fact]
    public async Task An_empty_change_sets_a_non_nullable_DateOnly_to_default()
    {
        var p = new Person { Name = "Ada", Age = 30, HireDate = new DateOnly(2020, 6, 1) };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.HireDate)]);

        var ok = await page.TryInvokeAsync(page.HandlerId("change")!, "{\"value\":\"\"}");

        Assert.True(ok);
        Assert.Equal(default, p.HireDate);
    }

    [Fact]
    public async Task An_empty_change_sets_a_non_nullable_enum_to_default()
    {
        var p = new Person { Name = "Ada", Age = 30, CurrentStatus = PersonStatus.Inactive };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.CurrentStatus)]);

        var ok = await page.TryInvokeAsync(page.HandlerId("change")!, "{\"value\":\"\"}");

        Assert.True(ok);
        Assert.Equal(default, p.CurrentStatus);
    }

    [Fact]
    public async Task Empty_input_sets_a_nullable_string_to_null()
    {
        // For `string?`, BindingHelpers.TrySetTyped reads the NRT annotation off the
        // PropertyInfo via NullabilityInfoContext and treats empty input as null. The
        // sibling test below pins the inverse for non-nullable `string`.
        var p = new Person { Name = "Ada", Age = 30, Nickname = "Bea" };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.Nickname)]);

        var inputId = page.HandlerId("input");
        Assert.NotNull(inputId);

        var ok = await page.TryInvokeAsync(inputId!, "{\"value\":\"\"}");

        Assert.True(ok);
        Assert.Null(p.Nickname);
    }

    [Fact]
    public async Task Empty_input_sets_a_non_nullable_string_to_an_empty_string()
    {
        // Non-nullable `string` keeps the pre-existing semantics — empty input becomes
        // empty string, not null. NullabilityInfoContext reports WriteState == NotNull
        // for the annotation, so the empty→null shortcut is skipped and the value flows
        // through RouteValueParser, which returns "" verbatim.
        var p = new Person { Name = "Ada", Age = 30 };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.Name)]);

        var inputId = page.HandlerId("input");
        Assert.NotNull(inputId);

        var ok = await page.TryInvokeAsync(inputId!, "{\"value\":\"\"}");

        Assert.True(ok);
        Assert.Equal("", p.Name);
    }

    [Fact]
    public async Task A_bool_checkbox_takes_the_reported_checked_state_and_is_self_correcting()
    {
        // BoolSetHandler (BindingHelpers.cs) sets the model to the checkbox's actual
        // reported state ("true"/"false" from rask.js) rather than flipping a captured
        // prior. This makes the binding self-correcting: sending the SAME state twice is
        // idempotent (it does not flip), so a one-step desync between el.checked and the
        // server model recovers on the next change event. The old blind toggle could not
        // recover — once drifted it kept inverting, which is the "checkbox sticks after a
        // few clicks" bug once clicks ship diffs (no checked re-base) instead of full HTML.
        var p = new Person { Name = "Ada", Age = 30, AcceptedTerms = null };
        var page = Test.Render(() => Form.Model(p)[Input.Bind(() => p.AcceptedTerms)]);

        var html = page.Html;
        var changeId = page.HandlerId("change");
        Assert.NotNull(changeId);

        async Task SendAsync(string value)
        {
            html = page.Render();
            changeId = Markup.Attr(html, "data-rask-on-change");
            await page.InvokeAsync(changeId!, $"{{\"value\":\"{value}\"}}");
        }

        await SendAsync("true");

        Assert.Equal(true, p.AcceptedTerms);

        // Idempotent: re-reporting "true" keeps it true (a blind toggle would flip to false).
        await SendAsync("true");

        Assert.Equal(true, p.AcceptedTerms);

        await SendAsync("false");

        Assert.Equal(false, p.AcceptedTerms);

        // Never resurrects null from the UI (HTML checkboxes have no indeterminate state).
        await SendAsync("false");

        Assert.Equal(false, p.AcceptedTerms);
        Assert.NotNull(p.AcceptedTerms);
    }

    private sealed class Person
    {
        [Required] public string Name { get; set; } = "";
        [Range(1, 120)] public int Age { get; set; }
        public bool Subscribed { get; set; }
        public decimal Salary { get; set; }
        public DateOnly HireDate { get; set; }
        public PersonStatus CurrentStatus { get; set; }

        public int? OptionalAge { get; set; }
        public decimal? Price { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateOnly? Birthday { get; set; }
        public bool? AcceptedTerms { get; set; }
        public string? Nickname { get; set; }
        public PersonStatus? Status { get; set; }
    }

    private enum PersonStatus { Active, Inactive }
}
