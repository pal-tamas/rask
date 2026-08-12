using System.ComponentModel.DataAnnotations;
using Rask.Example.Shared;

namespace Rask.Example.Shared.Features;

public sealed partial class FloatingLabelsDemo : Component
{
    private readonly AccountModel _model = new();
    private string? _submission;

    protected override Component? Render() =>
    [
        Form.Model(_model).OnValidSubmit(m => _submission = $"Created account for {m.FullName} <{m.Email}>").Class("vstack gap-2")[
            DataAnnotationsValidator,
            // One line per field — the Floating* components wrap Input/Select/Textarea + Label +
            // ValidationMessage in Bootstrap's .form-floating markup. The label is read from each
            // property's [Display(Name)], the input type is inferred from the property's CLR type,
            // and validation flows from the [Required]/[Range]/etc. attributes through
            // DataAnnotationsValidator(). Every property is nullable — Rask clears to null.
            FloatingInput.Bind(() => _model.FullName),
            FloatingInput.Bind(() => _model.Email),
            FloatingInput.Bind(() => _model.Age),
            FloatingSelect.Bind(() => _model.Plan)[
                Option.Value("")["— choose —"],
                Option.Value("free")["Free"],
                Option.Value("pro")["Pro"],
                Option.Value("team")["Team"]
            ],
            FloatingTextarea.Bind(() => _model.Bio),
            Div.Class("mt-1")[
                BsButton.Type("submit").Color(BsColor.Primary)[BsIcon.Name(BsIconName.PersonPlus).Class("me-1"), "Create account"]
            ]
        ],
        _submission is null
            ? null
            : BsAlert.Color(BsColor.Success).Class("small mt-3 mb-0")[BsIcon.Name(BsIconName.CheckCircle).Class("me-2"), _submission]
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
