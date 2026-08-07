using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

public sealed partial class AsyncValidationDemo : Component
{
    private readonly EditContext _ctx;
    private readonly SignupModel _model = new();
    private string? _submission;

    public AsyncValidationDemo()
    {
        _ctx = new EditContext(_model);
        _ctx.AddValidator(new UniqueUsernameValidator());
    }

    private static Component Checking() =>
        Span(Class: "validating-indicator text-muted small mt-1")[
            BsIcon(Name: BsIconName.ArrowClockwise, Class: "me-1"), "Checking availability..."
        ];

    protected override Component? Render() =>
    [
        Form<SignupModel>(
            _model,
            m => _submission = $"Signed up: {m.Username}",
            Context: _ctx,
            Class: "vstack gap-3")[
            DataAnnotationsValidator(),
            Div()[
                Label("v3-username", Class: "form-label small mb-1")["Username"],
                Input(() => _model.Username, Id: "v3-username", Class: "form-control"),
                ValidatingIndicator(() => _model.Username, Checking),
                ValidationMessage(() => _model.Username,
                    msgs => [.. msgs.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])])
            ],
            Div()[
                BsButton(Type: "submit", Color: BsColor.Primary)[BsIcon(Name: BsIconName.Check2Circle, Class: "me-1"), "Sign up"]
            ]
        ],
        _submission is null
            ? null
            : BsAlert(Color: BsColor.Success, Class: "small mt-3 mb-0")[BsIcon(Name: BsIconName.CheckCircle, Class: "me-2"), _submission]
    ];
}

public sealed class SignupModel
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Username must be 3–20 characters.")]
    public string Username { get; set; } = "";
}

public sealed class UniqueUsernameValidator : IAsyncFieldValidator
{
    private static readonly HashSet<string> Taken = new(StringComparer.OrdinalIgnoreCase) { "admin", "taken", "root" };

    public async ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken)
    {
        if (context.Model is SignupModel m)
        {
            await CheckAsync(context, new FieldIdentifier(m, nameof(SignupModel.Username)), m.Username,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field,
        CancellationToken cancellationToken)
    {
        if (context.Model is SignupModel m && field.FieldName == nameof(SignupModel.Username))
        {
            await CheckAsync(context, field, m.Username, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CheckAsync(EditContext context, FieldIdentifier field, string username,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        // E2E test seam: the literal "explode" forces the validator to throw mid-await so the
        // framework's generic "Validation could not be completed." path is exercised end-to-end.
        if (string.Equals(username, "explode", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Yield();
            throw new InvalidOperationException("Simulated remote failure.");
        }

        await Task.Delay(400, ct).ConfigureAwait(false);
        if (Taken.Contains(username))
        {
            context.AddValidationMessage(field, $"\"{username}\" is already taken.");
        }
    }
}
