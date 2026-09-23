using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

namespace Rask.Site.Features;

// IValidatableObject parity with ASP.NET Core: BookingModel mixes attribute rules ([Required]
// on Name) with an IValidatableObject.Validate method that yields both a per-field result
// (MemberNames=[nameof(Departure)]) and a form-level result (no MemberNames). The BCL's own
// Validator.TryValidateObject would silence Validate() once the attribute fails — Rask's
// built-in pass calls IValidatableObject directly so all errors accumulate.
public sealed partial class ValidatableObjectDemo : Component
{
    private readonly BookingModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])];

    private static Component? SummaryAlert(IReadOnlyList<ValidationEntry> entries)
    {
        var formOnly = entries.Where(e => e.Field.Length == 0).ToList();
        if (formOnly.Count == 0)
        {
            return null;
        }

        return Ui.Alert.Tone(Ui.Tone.Error).Variant(Ui.Variant.Soft).Class("text-sm mb-0")[Ul.Class("mb-0 ps-3")[formOnly.Select((e, i) => Li.Key(i)[e.Message])]];
    }

    protected override Component? Render() =>
    [
        Form.Model(_model).OnValidSubmit(m => _submission = $"Booked: {m.Name} {m.Departure:yyyy-MM-dd} → {m.Arrival:yyyy-MM-dd}").Class("flex flex-col gap-3")[
            Validation.Summary.Template(SummaryAlert),
            Div[
                Ui.Input.Bind(() => _model.Name).Label("Name").Id("v11-name").ShowValidation(false),
                Validation.Message.Template(FieldError).For(() => _model.Name)
            ],
            Div[
                Ui.Input.Bind(() => _model.Departure).Label("Departure").Id("v11-departure").ShowValidation(false),
                Validation.Message.Template(FieldError).For(() => _model.Departure)
            ],
            Div[
                Ui.Input.Bind(() => _model.Arrival).Label("Arrival").Id("v11-arrival").ShowValidation(false),
                Validation.Message.Template(FieldError).For(() => _model.Arrival)
            ],
            Div[
                Ui.Button.Tone(Ui.Tone.Primary).Type(Ui.ButtonType.Submit)[Ui.Icon.Name(Ui.IconName.Calendar), "Book"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Alert.Tone(Ui.Tone.Success).Variant(Ui.Variant.Soft).Class("text-sm mt-3 mb-0")[Ui.Icon.Name(Ui.IconName.CheckCircle), _submission]
    ];
}

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
