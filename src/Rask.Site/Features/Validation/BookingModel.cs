using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

namespace Rask.Site.Features;

public sealed class BookingModel : IValidatableObject
{
    private static readonly DateOnly Today = new(2026, 5, 14);

    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = "";

    public DateOnly Departure { get; set; } = new(2026, 7, 1);
    public DateOnly Arrival { get; set; } = new(2026, 7, 5);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Departure < Today)
        {
            yield return new ValidationResult(
                "Departure cannot be in the past.",
                new[] { nameof(Departure) });
        }

        if (Arrival <= Departure)
        {
            yield return new ValidationResult("Arrival must be after departure.");
        }
    }
}
