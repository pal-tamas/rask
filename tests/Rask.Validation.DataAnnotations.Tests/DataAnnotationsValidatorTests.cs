using System.ComponentModel.DataAnnotations;
using Rask.Core;
using Rask.Core.Forms;


namespace Rask.Validation.DataAnnotations.Tests;

public partial class DataAnnotationsValidatorTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task SubmitFlow_FirstInvalid_ThenFilled_RoutesToOnValidSubmit()
    {
        // Reproduces the showcase ValidationSummary demo flow as a unit test:
        //   1. Render Form (auto-attached via DataAnnotationsValidator child).
        //   2. Submit empty payload — must route to OnInvalidSubmit (which is null here,
        //      so neither typed handler fires; the bridge returns quietly).
        //   3. Re-render — the same EditContext must survive, the child re-registers
        //      idempotently, and the freshly-issued submit handler id must close over a
        //      context that still recognises the validator.
        //   4. Submit a valid payload via the new handler — must reach OnValidSubmit.
        var p = new Person { Name = "", Age = 0 };
        Person? captured = null;
        var page = RaskTest.Render(() => Form.Model(p).OnValidSubmit((Action<Person>)(m => captured = m))[
            DataAnnotationsValidator,
            Input.Bind(() => p.Name),
            Input.Bind(() => p.Age)
        ]);

        var submit1 = page.HandlerId("submit");
        Assert.NotNull(submit1);

        await page.InvokeAsync(submit1!, "{\"form\":{\"Name\":\"\",\"Age\":\"0\"}}");

        Assert.Null(captured);

        // Mutate the model out-of-band to simulate the user filling fields between renders
        // (the real Input bind events do this through NotifyFieldChanged; the second submit
        // is what we're stress-testing here, not the per-keystroke wiring).
        p.Name = "Ada";
        p.Age = 30;

        page.Render();
        var submit2 = page.HandlerId("submit");
        Assert.NotNull(submit2);

        await page.InvokeAsync(submit2!, "{\"form\":{\"Name\":\"Ada\",\"Age\":\"30\"}}");

        Assert.Same(p, captured);
    }

    [Fact]
    public void Validate_PopulatesMessages_PerOffendingMember()
    {
        var p = new Person { Name = "", Age = 0, Code = "" };
        var ctx = RegisterValidator(p);

        var ok = ctx.Validate();

        Assert.False(ok);
        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(p, "Age")));
    }

    [Fact]
    public void Validate_AllValid_ReturnsTrue()
    {
        var p = new Person { Name = "Ada", Age = 30, Code = "ABC" };
        var ctx = RegisterValidator(p);
        Assert.True(ctx.Validate());
        Assert.False(ctx.HasValidationMessages());
    }

    [Fact]
    public void ValidateField_OnlyValidatesThatField()
    {
        var p = new Person { Name = "", Age = 999, Code = "" };
        var ctx = RegisterValidator(p);

        ctx.ValidateField(new FieldIdentifier(p, "Age"));

        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(p, "Age")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    [Fact]
    public void Validate_IValidatableObject_FormLevelError_AttachesToEmptyField()
    {
        // Model.Validate returns a ValidationResult with empty MemberNames — should land on
        // FieldIdentifier(model, "") so ValidationSummary picks it up as a form-level error.
        var m = new BookingModel
        {
            Departure = new DateOnly(2026, 6, 1),
            Arrival = new DateOnly(2026, 6, 5),
            RaiseFormLevel = true
        };
        var ctx = RegisterValidator(m);

        ctx.Validate();

        Assert.Contains("Booking spans a blackout window.",
            ctx.GetValidationMessages(new FieldIdentifier(m, string.Empty)));
    }

    [Fact]
    public void Validate_IValidatableObject_PerFieldError_AttachesToNamedField()
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

        ctx.Validate();

        Assert.Contains("Departure cannot be in the past.",
            ctx.GetValidationMessages(new FieldIdentifier(m, nameof(BookingModel.Departure))));
    }

    [Fact]
    public void Validate_IValidatableObject_RunsEvenWhenAttributeValidationFails()
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

        ctx.Validate();

        Assert.Contains("Name is required.",
            ctx.GetValidationMessages(new FieldIdentifier(m, nameof(BookingModel.Name))));
        Assert.Contains("Booking spans a blackout window.",
            ctx.GetValidationMessages(new FieldIdentifier(m, string.Empty)));
    }

    [Fact]
    public void ValidateField_IValidatableObject_SurfacesCrossFieldErrorOnNamedField()
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

        ctx.ValidateField(new FieldIdentifier(m, nameof(BookingModel.Departure)));

        Assert.Contains("Departure cannot be in the past.",
            ctx.GetValidationMessages(new FieldIdentifier(m, nameof(BookingModel.Departure))));
    }

    [Fact]
    public void ValidateField_IValidatableObject_IgnoresErrorsForOtherFields()
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

        ctx.ValidateField(new FieldIdentifier(m, nameof(BookingModel.Name)));

        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(m, nameof(BookingModel.Name))));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(m, nameof(BookingModel.Departure))));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(m, string.Empty)));
    }

    [Fact]
    public void Component_Render_IsIdempotent_AcrossMultipleRenders()
    {
        var p = new Person { Name = "" };
        EditContext? captured = null;

        // Two separate component instances each Render under the same context: AddValidator's
        // type-dedup should prevent double-registration. If duplicated, "Name is required"
        // would appear twice in the messages list.
        RaskTest.Render(() => Form.Model(p)[
            DataAnnotationsValidator,
            DataAnnotationsValidator,
            RaskTest.EditContextProbe(c => captured = c)
        ]);
        var ctx = captured!;

        ctx.Validate();
        Assert.Single(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
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
