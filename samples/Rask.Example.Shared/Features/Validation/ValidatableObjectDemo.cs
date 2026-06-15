using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

// IValidatableObject parity with ASP.NET Core: BookingModel mixes attribute rules ([Required]
// on Name) with an IValidatableObject.Validate method that yields both a per-field result
// (MemberNames=[nameof(Departure)]) and a form-level result (no MemberNames). The BCL's own
// Validator.TryValidateObject would silence Validate() once the attribute fails — Rask's
// DataAnnotationsValidator calls IValidatableObject directly so all errors accumulate.
public sealed class ValidatableObjectDemo : Component
{
    private readonly BookingModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])];

    private static Component SummaryAlert(IReadOnlyList<ValidationEntry> entries)
    {
        var formOnly = entries.Where(e => e.Field.Length == 0).ToList();
        if (formOnly.Count == 0)
        {
            return Fragment();
        }

        return Div(Class: "alert alert-danger small mb-0")[
            Ul(Class: "mb-0 ps-3")[formOnly.Select((e, i) => Li(Key: i)[e.Message])]
        ];
    }

    protected override RenderResult Render() =>
    [
        Form<BookingModel>(
            _model,
            m => _submission = $"Booked: {m.Name} {m.Departure:yyyy-MM-dd} → {m.Arrival:yyyy-MM-dd}",
            Class: "vstack gap-3")[
            DataAnnotationsValidator(),
            ValidationSummary(SummaryAlert),
            Div()[
                Label("v11-name", Class: "form-label small mb-1")["Name"],
                Input(() => _model.Name, Id: "v11-name", Class: "form-control"),
                ValidationMessage(() => _model.Name, FieldError)
            ],
            Div()[
                Label("v11-departure", Class: "form-label small mb-1")["Departure"],
                Input(() => _model.Departure, Id: "v11-departure", Class: "form-control"),
                ValidationMessage(() => _model.Departure, FieldError)
            ],
            Div()[
                Label("v11-arrival", Class: "form-label small mb-1")["Arrival"],
                Input(() => _model.Arrival, Id: "v11-arrival", Class: "form-control"),
                ValidationMessage(() => _model.Arrival, FieldError)
            ],
            Div()[
                Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-calendar-check me-1"), "Book"]
            ]
        ],
        _submission is null
            ? Fragment()
            : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]
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
