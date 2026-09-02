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
        Span.Class("validating-indicator text-slate-500 dark:text-slate-400 text-sm mt-1")[
            Icon.Name(IconName.ArrowClockwise).Class("me-1"), "Checking availability..."
        ];

    protected override Component? Render() =>
    [
        Form.Model(_model).OnValidSubmit(m => _submission = $"Signed up: {m.Username}").Context(_ctx).Class("flex flex-col gap-3")[
            DataAnnotationsValidator,
            Div[
                Label.For("v3-username").Class($"{Tw.Label} text-sm mb-1")["Username"],
                Input.Bind(() => _model.Username).Id("v3-username").Class(Tw.Input),
                ValidatingIndicator.Template(Checking).For(() => _model.Username),
                ValidationMessage.Template(msgs => [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])])
                    .For(() => _model.Username)
            ],
            Div[
                Button.Class(Tw.BtnPrimary).Type("submit")[Icon.Name(IconName.Check2Circle).Class("me-1"), "Sign up"]
            ]
        ],
        _submission is null
            ? null
            : Div.Role("status").Class($"{Tw.AlertSuccess} text-sm mt-3 mb-0")[Icon.Name(IconName.CheckCircle).Class("me-2"), _submission]
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
