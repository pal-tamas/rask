using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

namespace Rask.Example.Shared.Features;

public sealed partial class ProgrammaticValidateDemo : Component
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
        [.. msgs.Select((m, i) => Div.Key(i).Class("text-danger text-sm mt-1")[m])];

    private static Component Checking() =>
        Span.Class("validating-indicator text-slate-500 dark:text-slate-400 text-sm mt-1")[
            Icon.Name(IconName.ArrowClockwise).Class("me-1"), "Checking…"
        ];

    private async Task ValidateNowAsync() => await _ctx.ValidateAsync().ConfigureAwait(false);

    protected override Component? Render() =>
    [
        Form.Model(_model).OnValidSubmit(m => _submission = $"Saved task: {m.Title}").Context(_ctx).Class("flex flex-col gap-3")[
            Div[
                Label.For("v6-title").Class($"{Ui.Label} text-sm mb-1")["Title"],
                Input.Bind(() => _model.Title).Id("v6-title").Class(Ui.Input),
                ValidatingIndicator.Template(Checking).For(() => _model.Title),
                ValidationMessage.Template(FieldError).For(() => _model.Title)
            ],
            Div.Class("flex gap-2 flex-wrap items-center")[
                Button.Type("button").Class(Ui.BtnOutlineSecondary).Id("v6-validate-now").OnClickAsync(ValidateNowAsync)[
                    Icon.Name(IconName.Search).Class("me-1"), "Validate now"
                ],
                Button.Class(Ui.BtnPrimary).Type("submit").Id("v6-submit").Disabled(_ctx.IsValidatingAny)[Icon.Name(IconName.Check2Circle).Class("me-1"), "Save"]
            ]
        ],
        _submission is null
            ? null
            : Div.Role("status").Class($"{Ui.AlertSuccess} text-sm mt-3 mb-0")[Icon.Name(IconName.CheckCircle).Class("me-2"), _submission]
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
