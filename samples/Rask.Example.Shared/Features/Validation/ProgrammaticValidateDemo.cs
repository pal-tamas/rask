using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

public sealed class ProgrammaticValidateDemo : Component
{
    private readonly EditContext _ctx;
    private readonly TaskModel _model = new();
    private string? _submission;

    public ProgrammaticValidateDemo()
    {
        _ctx = new EditContext(_model);
        _ctx.AddValidator(new SlowTitleValidator());
    }

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])];

    private static Component Checking() =>
        Span(Class: "validating-indicator text-muted small mt-1")[
            I(Class: "bi bi-arrow-clockwise me-1"), "Checking…"
        ];

    private async Task ValidateNowAsync() => await _ctx.ValidateAsync().ConfigureAwait(false);

    protected override RenderResult Render() =>
    [
        Form<TaskModel>(
            _model,
            m => _submission = $"Saved task: {m.Title}",
            Context: _ctx,
            Class: "vstack gap-3")[
            Div()[
                Label("v6-title", Class: "form-label small mb-1")["Title"],
                Input(() => _model.Title, Id: "v6-title", Class: "form-control"),
                ValidatingIndicator(() => _model.Title, Checking),
                ValidationMessage(() => _model.Title, FieldError)
            ],
            Div(Class: "d-flex gap-2")[
                Button(
                    "button",
                    Id: "v6-validate-now",
                    Class: "btn btn-outline-secondary",
                    OnClickAsync: ValidateNowAsync)[
                    I(Class: "bi bi-search me-1"), "Validate now"
                ],
                Button(
                    "submit",
                    Id: "v6-submit",
                    Disabled: _ctx.IsValidatingAny,
                    Class: "btn btn-primary")[I(Class: "bi bi-check2-circle me-1"), "Save"]
            ]
        ],
        _submission is null
            ? Fragment()
            : BsAlert(Color: BsColor.Success, Class: "small mt-3 mb-0")[I(Class: "bi bi-check-circle me-2"), _submission]
    ];
}

public sealed class TaskModel
{
    [Required(ErrorMessage = "Title is required.")]
    public string Title { get; set; } = "";
}

// 600ms delay so the e2e test for submit-disable has a deterministic window to observe
// the disabled state before the async validator settles. Like UniqueUsernameValidator,
// the literal "explode" exercises the framework's exception fallback.
public sealed class SlowTitleValidator : IAsyncFieldValidator
{
    public async ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken)
    {
        if (context.Model is TaskModel m)
        {
            await CheckAsync(context, new FieldIdentifier(m, nameof(TaskModel.Title)), m.Title, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field,
        CancellationToken cancellationToken)
    {
        if (context.Model is TaskModel m && field.FieldName == nameof(TaskModel.Title))
        {
            await CheckAsync(context, field, m.Title, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CheckAsync(EditContext context, FieldIdentifier field, string title, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        await Task.Delay(600, ct).ConfigureAwait(false);
        if (string.Equals(title, "duplicate", StringComparison.OrdinalIgnoreCase))
        {
            context.AddValidationMessage(field, $"\"{title}\" is already used.");
        }
    }
}
