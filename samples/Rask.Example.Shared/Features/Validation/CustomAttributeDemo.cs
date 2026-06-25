using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Rask.Example.Shared.Features;

// Custom ValidationAttribute showcase. Three flavors flow through Rask's DataAnnotationsValidator
// unchanged because System.ComponentModel.DataAnnotations.Validator walks every attribute on the
// property — there's no opt-in needed for user-authored subclasses:
//   • StrongPassword overrides IsValid(object?) — the simplest shape.
//   • MatchesProperty overrides GetValidationResult(object?, ValidationContext) — uses
//     ValidationContext.ObjectInstance to do cross-field comparison.
//   • NotBanned overrides GetValidationResult and resolves IBannedWordService via
//     ValidationContext.GetService<T>() — proves the render-scoped IServiceProvider flows through.
public sealed class CustomAttributeDemo : Component
{
    private readonly CustomAttributeModel _model = new();
    private string? _submission;

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])];

    protected override RenderResult Render() =>
    [
        Form<CustomAttributeModel>(
            _model,
            m => _submission = $"Welcome, {m.Username}!",
            Class: "vstack gap-3")[
            DataAnnotationsValidator(),
            Div()[
                Label("v12-username", Class: "form-label small mb-1")["Username"],
                Input(() => _model.Username, Id: "v12-username", Class: "form-control"),
                ValidationMessage(() => _model.Username, FieldError)
            ],
            Div()[
                Label("v12-password", Class: "form-label small mb-1")["Password"],
                Input(() => _model.Password, Id: "v12-password", Type: InputType.Password, Class: "form-control"),
                ValidationMessage(() => _model.Password, FieldError)
            ],
            Div()[
                Label("v12-confirm", Class: "form-label small mb-1")["Confirm password"],
                Input(() => _model.ConfirmPassword, Id: "v12-confirm", Type: InputType.Password, Class: "form-control"),
                ValidationMessage(() => _model.ConfirmPassword, FieldError)
            ],
            Div()[
                Button("submit", Class: "btn btn-primary")[I(Class: "bi bi-shield-check me-1"), "Create account"]
            ]
        ],
        _submission is null
            ? Fragment()
            : Div(Class: "alert alert-success small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]
    ];
}

public sealed class CustomAttributeModel
{
    [Required(ErrorMessage = "Username is required.")]
    [NotBanned(ErrorMessage = "\"{0}\" isn't available.")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Password is required.")]
    [StrongPassword(ErrorMessage = "Password must be at least 8 characters and mix letters and digits.")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Please confirm your password.")]
    [MatchesProperty(nameof(Password), ErrorMessage = "Passwords don't match.")]
    public string ConfirmPassword { get; set; } = "";
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class StrongPasswordAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is not string s || s.Length < 8)
        {
            return false;
        }

        bool hasLetter = false, hasDigit = false;
        foreach (var ch in s)
        {
            if (char.IsLetter(ch))
            {
                hasLetter = true;
            }
            else if (char.IsDigit(ch))
            {
                hasDigit = true;
            }

            if (hasLetter && hasDigit)
            {
                return true;
            }
        }

        return false;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class MatchesPropertyAttribute(string otherProperty) : ValidationAttribute
{
    public string OtherProperty { get; } = otherProperty;

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification =
            "GetProperty on the model's runtime type — the model is preserved by the user's binding setup, same contract as the validator itself.")]
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var instance = validationContext.ObjectInstance;
        var sibling = instance.GetType().GetProperty(OtherProperty);
        if (sibling is null)
        {
            return new ValidationResult($"Unknown property '{OtherProperty}'.");
        }

        var other = sibling.GetValue(instance);
        return Equals(value, other)
            ? ValidationResult.Success
            : new ValidationResult(ErrorMessage ?? $"Must match {OtherProperty}.",
                validationContext.MemberName is null ? null : new[] { validationContext.MemberName });
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class NotBannedAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // No SP, no enforcement — the rule degrades gracefully when the host hasn't registered
        // the service. ASP.NET Core MVC's own attributes behave the same way when GetService
        // returns null. This means tests that bypass the live render path see the attribute
        // pass for any value; the dedicated DI test pushes a LiveRenderContext to opt in.
        var svc = (IBannedWordService?)validationContext.GetService(typeof(IBannedWordService));
        if (svc is null || value is not string s || s.Length == 0)
        {
            return ValidationResult.Success;
        }

        return svc.Words.Contains(s)
            ? new ValidationResult(FormatErrorMessage(s),
                validationContext.MemberName is null ? null : new[] { validationContext.MemberName })
            : ValidationResult.Success;
    }
}
