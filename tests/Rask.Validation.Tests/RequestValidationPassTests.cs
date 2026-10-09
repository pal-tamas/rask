using System.ComponentModel.DataAnnotations;

namespace Rask.ValidationTests;

// The static pass, used by the CQRS request validators. It has no EditContext behind it, so it does
// NOT get the dedup that AddValidationMessage gives the form path — which is exactly why the two bugs
// below were invisible until this entry point existed. These assert on the returned entries directly.
public class RequestValidationPassTests
{
    [Fact]
    public void An_object_level_rule_is_reported_once_not_twice()
    {
        // Validator.TryValidateObject ALREADY runs IValidatableObject when no attribute failed, so
        // calling it again for MVC parity duplicated every object-level failure. The form never showed
        // it (same message, same field, dropped on add); a dispatched request put it on the wire twice.
        var entries = DataAnnotationsFieldValidator.Validate(new Booking { Name = "Ada", Blackout = true });

        Assert.Equal(["Booking spans a blackout window."], entries.Select(e => e.Message));
    }

    [Fact]
    public void An_object_level_rule_still_runs_when_an_attribute_also_failed()
    {
        // The MVC parity the double call was there for: the BCL stops after the attribute failure, so
        // the object-level rule has to be invoked by hand in THIS case — and only this case.
        var entries = DataAnnotationsFieldValidator.Validate(new Booking { Name = "", Blackout = true });

        Assert.Contains("Name is required.", entries.Select(e => e.Message));
        Assert.Contains("Booking spans a blackout window.", entries.Select(e => e.Message));
    }

    [Fact]
    public void A_validatable_object_yielding_success_does_not_throw()
    {
        // ValidationResult.Success IS null. Yielding it is legal and the BCL filters it; this pass used
        // to add it to the list and then dereference it — a 400 turning into a 500 on the request path.
        var entries = DataAnnotationsFieldValidator.Validate(new YieldsSuccess());

        Assert.Equal(["Only the real one."], entries.Select(e => e.Message));
    }

    [Fact]
    public void A_form_level_rule_lands_on_the_empty_field_key()
    {
        var entries = DataAnnotationsFieldValidator.Validate(new Booking { Name = "Ada", Blackout = true });

        Assert.Equal(string.Empty, Assert.Single(entries).Field);
    }

    // What the pass records TODAY for a member that is not on the root: its own name, with nothing of the
    // way to it. The same string is the key of a request's `errors` in the 400 problem document, so it is
    // pinned here: making it a path (`Lines[1].ValidFrom`) changes what every API caller reads.
    [Fact]
    public void A_member_of_a_nested_object_is_recorded_by_its_own_name_without_the_path_to_it()
    {
        var entries = DataAnnotationsFieldValidator.Validate(new Order { Price = new Money { Currency = "" } });

        Assert.Equal(["Currency"], entries.Select(e => e.Field));
    }

    [Fact]
    public void A_member_of_a_row_is_recorded_by_its_own_name_so_two_rows_are_told_apart_by_nothing()
    {
        var order = new Order { Lines = [new Line { ValidFrom = "" }, new Line { ValidFrom = "" }] };

        var entries = DataAnnotationsFieldValidator.Validate(order);

        Assert.Equal(["ValidFrom", "ValidFrom"], entries.Select(e => e.Field));
    }

    private sealed class Order
    {
        public Money Price { get; set; } = new();

        public List<Line> Lines { get; set; } = [];
    }

    private sealed class Money
    {
        [Required]
        public string Currency { get; set; } = "HUF";
    }

    private sealed class Line
    {
        [Required]
        public string ValidFrom { get; set; } = "2026-10-01";
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
