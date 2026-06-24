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
            // One line per field — FloatingInput wraps Input + Label + ValidationMessage in
            // Bootstrap's .form-floating markup. The type is inferred from the property (FullName/
            // Email are text, Age is number); validation flows from the [Required]/[Range]/etc.
            // attributes on AccountModel through DataAnnotationsValidator().
            FloatingInput(() => _model.FullName, "Full name"),
            FloatingInput(() => _model.Email, "Email address"),
            FloatingInput(() => _model.Age, "Age"),
            Div(Class: "mt-1")[
                Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-person-plus me-1"), "Create account"]
            ]
        ],
        _submission is null
            ? Fragment()
            : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]
    ];
}

public sealed class AccountModel
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "Full name must be 2–60 characters.")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = "";

    [Range(18, 120, ErrorMessage = "Age must be between 18 and 120.")]
    public int Age { get; set; }
}
