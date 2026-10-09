using System.ComponentModel.DataAnnotations;

namespace Rask.ValidationTests;

public partial class DataAnnotationsValidatorTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_form_submitted_invalid_then_filled_in_reaches_OnSubmit()
    {
        // Reproduces the showcase Validation.Summary demo flow as a unit test:
        //   1. Render Form — the validator is registered by the form itself, nothing declared.
        //   2. Submit empty payload — must route to OnInvalidSubmit (which is null here,
        //      so neither typed handler fires; the bridge returns quietly).
        //   3. Re-render — the same EditContext must survive, the form re-registers
        //      idempotently, and the freshly-issued submit handler id must close over a
        //      context that still recognises the validator.
        //   4. Submit a valid payload via the new handler — must reach OnSubmit.
        var p = new Person { Name = "", Age = 0 };
        Person? captured = null;
        var page = Page.Render(() => Form.Model(p).OnSubmit((Action<Person>)(m => captured = m))[
            Input.Bind(() => p.Name),
            Input.Bind(() => p.Age)
        ]);
        var submit1 = page.HandlerId("submit");

        Assert.NotNull(submit1);

        await page.Invoke(submit1!, "{\"form\":{\"Name\":\"\",\"Age\":\"0\"}}");

        Assert.Null(captured);

        // Mutate the model out-of-band to simulate the user filling fields between renders
        // (the real Input bind events do this through NotifyFieldChanged; the second submit
        // is what we're stress-testing here, not the per-keystroke wiring).
        p.Name = "Ada";
        p.Age = 30;

        page.Render();
        var submit2 = page.HandlerId("submit");

        Assert.NotNull(submit2);

        await page.Invoke(submit2!, "{\"form\":{\"Name\":\"Ada\",\"Age\":\"30\"}}");

        Assert.Same(p, captured);
    }

    [Fact]
    public async Task Validating_adds_a_message_for_each_offending_member()
    {
        var p = new Person { Name = "", Age = 0, Code = "" };
        var ctx = RegisterValidator(p);

        var ok = await ctx.Validate(TestContext.Current.CancellationToken);

        Assert.False(ok);
        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(p, "Age")));
    }

    [Fact]
    public async Task Validating_an_all_valid_model_returns_true()
    {
        var p = new Person { Name = "Ada", Age = 30, Code = "ABC" };
        var ctx = RegisterValidator(p);

        Assert.True(await ctx.Validate(TestContext.Current.CancellationToken));
        Assert.False(ctx.HasValidationMessages());
    }

    [Fact]
    public async Task Validating_one_field_validates_only_that_field()
    {
        var p = new Person { Name = "", Age = 999, Code = "" };
        var ctx = RegisterValidator(p);

        await ctx.ValidateField(new FieldIdentifier(p, "Age"), TestContext.Current.CancellationToken);

        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(p, "Age")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    [Fact]
    public async Task A_form_level_IValidatableObject_error_attaches_to_the_empty_field()
    {
        // Model.Validate returns a ValidationResult with empty MemberNames — should land on
        // FieldIdentifier(model, "") so Validation.Summary picks it up as a form-level error.
        var m = new BookingModel
        {
            Departure = new DateOnly(2026, 6, 1),
            Arrival = new DateOnly(2026, 6, 5),
            RaiseFormLevel = true
        };
        var ctx = RegisterValidator(m);

        await ctx.Validate(TestContext.Current.CancellationToken);

        Assert.Contains("Booking spans a blackout window.",
            ctx.GetValidationMessages(new FieldIdentifier(m, string.Empty)));
    }

    [Fact]
    public async Task A_per_field_IValidatableObject_error_attaches_to_the_named_field()
    {
        // Model.Validate returns a ValidationResult with MemberNames = ["Departure"] — should
        // land on that field's messages.
        var m = new BookingModel
        {
            Name = "Ada",
            Departure = new DateOnly(2020, 1, 1),
            Arrival = new DateOnly(2026, 6, 5)
        };
        var ctx = RegisterValidator(m);

        await ctx.Validate(TestContext.Current.CancellationToken);

        Assert.Contains("Departure cannot be in the past.",
            ctx.GetValidationMessages(new FieldIdentifier(m, nameof(BookingModel.Departure))));
    }

    [Fact]
    public async Task IValidatableObject_runs_even_when_attribute_validation_fails()
    {
        // ASP.NET Core parity: BCL's TryValidateObject silences IValidatableObject as soon as
        // any attribute fails. Here Name is empty (Required fails) AND the model raises a
        // form-level error — both must surface together.
        var m = new BookingModel
        {
            Name = "",
            Departure = new DateOnly(2026, 6, 1),
            Arrival = new DateOnly(2026, 6, 5),
            RaiseFormLevel = true
        };
        var ctx = RegisterValidator(m);

        await ctx.Validate(TestContext.Current.CancellationToken);

        Assert.Contains("Name is required.",
            ctx.GetValidationMessages(new FieldIdentifier(m, nameof(BookingModel.Name))));
        Assert.Contains("Booking spans a blackout window.",
            ctx.GetValidationMessages(new FieldIdentifier(m, string.Empty)));
    }

    [Fact]
    public async Task Validating_one_field_surfaces_the_IValidatableObject_cross_field_error_on_it()
    {
        // Re-validating Departure on its own should surface the IValidatableObject result
        // whose MemberNames include Departure.
        var m = new BookingModel
        {
            Name = "Ada",
            Departure = new DateOnly(2020, 1, 1),
            Arrival = new DateOnly(2026, 6, 5)
        };
        var ctx = RegisterValidator(m);

        await ctx.ValidateField(new FieldIdentifier(m, nameof(BookingModel.Departure)), TestContext.Current.CancellationToken);

        Assert.Contains("Departure cannot be in the past.",
            ctx.GetValidationMessages(new FieldIdentifier(m, nameof(BookingModel.Departure))));
    }

    [Fact]
    public async Task Validating_one_field_ignores_IValidatableObject_errors_for_other_fields()
    {
        // Re-validating Name must NOT pull in Departure's IValidatableObject error, and a
        // form-level (empty MemberNames) error must not attach to Name either.
        var m = new BookingModel
        {
            Name = "Ada",
            Departure = new DateOnly(2020, 1, 1),
            Arrival = new DateOnly(2026, 6, 5),
            RaiseFormLevel = true
        };
        var ctx = RegisterValidator(m);

        await ctx.ValidateField(new FieldIdentifier(m, nameof(BookingModel.Name)), TestContext.Current.CancellationToken);

        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(m, nameof(BookingModel.Name))));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(m, nameof(BookingModel.Departure))));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(m, string.Empty)));
    }

    [Fact]
    public async Task Registering_the_validator_is_idempotent_across_multiple_renders()
    {
        var p = new Person { Name = "" };
        var ctx = new EditContext(p);

        // The form registers the built-in validator on EVERY render, which is only safe because
        // AddValidator dedups by runtime type. Two renders sharing one context is the cheapest way to
        // hold that: if the dedup ever stops working, "Name is required" appears twice rather than once
        // — and it would appear once per re-render in a real app, which is every keystroke.
        Page.Render(() => Form.Model(p).Context(ctx)[
            Test.EditContextProbe(_ => { })
        ]);
        Page.Render(() => Form.Model(p).Context(ctx)[
            Test.EditContextProbe(_ => { })
        ]);

        await ctx.Validate(TestContext.Current.CancellationToken);

        Assert.Single(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    [Fact]
    public async Task AutoValidate_false_takes_the_form_out_of_validation()
    {
        // Nothing declared means nothing to delete when you want out, so the opt-out is the only thing
        // an author writes — and it has to actually stop the pass, not just stop reporting it.
        var p = new Person { Name = "" };
        var ctx = WithoutAutoValidation(p);

        await ctx.Validate(TestContext.Current.CancellationToken);

        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    [Fact]
    public async Task The_global_RaskValidation_AutoValidate_false_takes_every_form_out_of_validation()
    {
        var p = new Person { Name = "" };

        RaskValidation.AutoValidate = false;
        try
        {
            var ctx = RegisterValidator(p);

            await ctx.Validate(TestContext.Current.CancellationToken);

            Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
        }
        finally
        {
            RaskValidation.AutoValidate = true;
        }
    }

    // The switch stops the rules nobody wrote in the app. A property's own rule and a store's were written —
    // on the type, on the index — and stay on.
    [Fact]
    public async Task The_global_AutoValidate_false_leaves_a_propertys_own_rule_and_the_stores_rules_on()
    {
        RaskValidation.RegisterFieldRules(typeof(Registered), Registered.RuleOf);
        RaskValidation.RegisterStoreRules(typeof(Registered), _ => new RegisteredStore());
        var m = new Registered();
        EditContext? ctx = null;

        RaskValidation.AutoValidate = false;
        try
        {
            var page = Page.Render(() => Form.Model(m)[
                Input.Bind(() => m.Name).Id("name").Blur(),
                Input.Bind(() => m.Code).Id("code").Blur(),
                Test.EditContextProbe(c => ctx = c)
            ]);

            await page.On("#name").Change("");
            await page.On("#code").Change("X1");

            Assert.Equal(["Name is required."], ctx!.GetValidationMessages(new FieldIdentifier(m, "Name")));
            Assert.Equal(["That code is taken."], ctx.GetValidationMessages(new FieldIdentifier(m, "Code")));
        }
        finally
        {
            RaskValidation.AutoValidate = true;
        }
    }

    private sealed class Registered
    {
        private static readonly Validate<string> NameRule = value => value.Length == 0 ? ["Name is required."] : [];

        public string Name { get; set; } = "Ada";

        public string Code { get; set; } = "";

        internal static Delegate? RuleOf(string property) => property == nameof(Name) ? NameRule : null;
    }

    private sealed class RegisteredStore : IStoreRules
    {
        public ValueTask<IReadOnlyList<Rask.Wire.FieldFailure>> Check(object model, string? field, CancellationToken cancellationToken) =>
            new(field == "Code" ? [new Rask.Wire.FieldFailure("That code is taken.", ["Code"])] : []);
    }

    private sealed class BookingModel : IValidatableObject
    {
        [Required(ErrorMessage = "Name is required.")]
        public string Name { get; set; } = "";

        public DateOnly Departure { get; set; }
        public DateOnly Arrival { get; set; }

        public bool RaiseFormLevel { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Departure < new DateOnly(2026, 1, 1))
            {
                yield return new ValidationResult(
                    "Departure cannot be in the past.",
                    new[] { nameof(Departure) });
            }

            if (RaiseFormLevel)
            {
                yield return new ValidationResult("Booking spans a blackout window.");
            }
        }
    }

    private sealed class Person
    {
        [Required(ErrorMessage = "Name is required")]
        public string Name { get; set; } = "";

        [Range(1, 120)] public int Age { get; set; }
        [StringLength(5)] public string Code { get; set; } = "";
    }
}
