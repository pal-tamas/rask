using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

namespace Rask.Site.Features;

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

    private async Task ValidateNowAsync() => await _ctx.ValidateAsync().ConfigureAwait(false);

    protected override Component? Render() =>
    [
        Form.Model(_model).OnValidSubmit(m => _submission = $"Saved task: {m.Title}").Context(_ctx).Class("flex flex-col gap-3")[
            // The kit field shows "Checking…" while SlowTitleValidator runs, whether a keystroke or the
            // button below started it; IsValidatingAny is what holds Save back until it settles.
            UiInput.Bind(() => _model.Title).Label("Title").Id("v6-title"),
            Div.Class("flex gap-2 flex-wrap items-center")[
                UiButton.Variant(UiVariant.Outline).Id("v6-validate-now").OnClick(ValidateNowAsync)[UiIcon.Name(UiIconName.Search), "Validate now"],
                UiButton.Tone(UiTone.Primary).Type(UiButtonType.Submit).Id("v6-submit").Disabled(_ctx.IsValidatingAny)[UiIcon.Name(UiIconName.CheckCircle), "Save"]
            ]
        ],
        _submission is null
            ? null
            : UiAlert.Tone(UiTone.Success).Variant(UiVariant.Soft).Class("text-sm mt-3 mb-0")[UiIcon.Name(UiIconName.CheckCircle), _submission]
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
