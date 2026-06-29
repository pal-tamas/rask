using System.ComponentModel.DataAnnotations;
using Rask.Example.Shared;

namespace Rask.Example.Shared.Features;

public sealed class FloatingLabelsDemo : Component
{
    private readonly AccountModel _model = new();
    private string? _submission;

    protected override RenderResult Render() =>
    [
        Form<AccountModel>(
            _model,
            m => _submission = $"Created account for {m.FullName} <{m.Email}>",
            Class: "vstack gap-2")[
            DataAnnotationsValidator(),
            // One line per field — the Floating* components wrap Input/Select/Textarea + Label +
            // ValidationMessage in Bootstrap's .form-floating markup. The label is read from each
            // property's [Display(Name)], the input type is inferred from the property's CLR type,
            // and validation flows from the [Required]/[Range]/etc. attributes through
            // DataAnnotationsValidator(). Every property is nullable — Rask clears to null.
            FloatingInput(() => _model.FullName),
            FloatingInput(() => _model.Email),
            FloatingInput(() => _model.Age),
            FloatingSelect(() => _model.Plan)[
                Option("")["— choose —"],
                Option("free")["Free"],
                Option("pro")["Pro"],
                Option("team")["Team"]
            ],
            FloatingTextarea(() => _model.Bio),
            Div(Class: "mt-1")[
                BsButton(Type: "submit", Color: BsColor.Primary)[I(Class: "bi bi-person-plus me-1"), "Create account"]
            ]
        ],
        _submission is null
            ? Fragment()
            : BsAlert(Color: BsColor.Success, Class: "small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]
    ];
}

public sealed class AccountModel
{
    [Display(Name = "Full name")]
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "Full name must be 2–60 characters.")]
    public string? FullName { get; set; }

    [Display(Name = "Email address")]
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string? Email { get; set; }

    [Display(Name = "Age")]
    [Range(18, 120, ErrorMessage = "Age must be between 18 and 120.")]
    public int? Age { get; set; }

    [Display(Name = "Plan")]
    [Required(ErrorMessage = "Pick a plan.")]
    public string? Plan { get; set; }

    [Display(Name = "Short bio")]
    [StringLength(200, ErrorMessage = "Bio must be 200 characters or fewer.")]
    public string? Bio { get; set; }
}
