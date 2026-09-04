using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

namespace Rask.Validation.Tests;

// The static pass, used by the CQRS request validators. It has no EditContext behind it, so it does
// NOT get the dedup that AddValidationMessage gives the form path — which is exactly why the two bugs
// below were invisible until this entry point existed. These assert on the returned entries directly.
public class RequestValidationPassTests
{
    [Fact]
    public void AnObjectLevelRule_IsReportedOnce_NotTwice()
    {
        // Validator.TryValidateObject ALREADY runs IValidatableObject when no attribute failed, so
        // calling it again for MVC parity duplicated every object-level failure. The form never showed
        // it (same message, same field, dropped on add); a dispatched request put it on the wire twice.
        var entries = DataAnnotationsFieldValidator.Validate(new Booking { Name = "Ada", Blackout = true });

        Assert.Equal(["Booking spans a blackout window."], entries.Select(e => e.Message));
    }

    [Fact]
    public void AnObjectLevelRule_StillRuns_WhenAnAttributeAlsoFailed()
    {
        // The MVC parity the double call was there for: the BCL stops after the attribute failure, so
        // the object-level rule has to be invoked by hand in THIS case — and only this case.
        var entries = DataAnnotationsFieldValidator.Validate(new Booking { Name = "", Blackout = true });

        Assert.Contains("Name is required.", entries.Select(e => e.Message));
        Assert.Contains("Booking spans a blackout window.", entries.Select(e => e.Message));
    }

    [Fact]
    public void AValidatableObject_YieldingSuccess_DoesNotThrow()
    {
        // ValidationResult.Success IS null. Yielding it is legal and the BCL filters it; this pass used
        // to add it to the list and then dereference it — a 400 turning into a 500 on the request path.
        var entries = DataAnnotationsFieldValidator.Validate(new YieldsSuccess());

        Assert.Equal(["Only the real one."], entries.Select(e => e.Message));
    }

    [Fact]
    public void AFormLevelRule_LandsOnTheEmptyFieldKey()
    {
        var entries = DataAnnotationsFieldValidator.Validate(new Booking { Name = "Ada", Blackout = true });

        Assert.Equal(string.Empty, Assert.Single(entries).Field);
    }

    private sealed class Booking : IValidatableObject
    {
        [Required(ErrorMessage = "Name is required.")]
        public string Name { get; set; } = "";

        public bool Blackout { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (this.Blackout)
            {
                yield return new ValidationResult("Booking spans a blackout window.");
            }
        }
    }

    private sealed class YieldsSuccess : IValidatableObject
    {
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            yield return ValidationResult.Success!;
            yield return new ValidationResult("Only the real one.");
        }
    }
}
