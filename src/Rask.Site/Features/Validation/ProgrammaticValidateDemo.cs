using Rask.Core.Forms;

namespace Rask.Site.Features;

public sealed partial class ProgrammaticValidateDemo : Component, IDisposable
{
    private readonly EditContext _ctx;
    private readonly TaskModel _model = new();
    private string? _submission;

    public ProgrammaticValidateDemo()
    {
        _ctx = new EditContext(_model);
        _ctx.AddValidator(new SlowTitleValidator());
    }

    private async Task ValidateNow() => await _ctx.Validate().ConfigureAwait(false);

    public void Dispose() => _ctx.Dispose();

    protected override Component? Render() =>
    [
        Form.Model(_model).OnSubmit(m => _submission = $"Saved task: {m.Title}").Context(_ctx).Class("flex flex-col gap-3")[
            // The kit field shows "Checking…" while SlowTitleValidator runs, whether a keystroke or the
            // button below started it; IsValidatingAny is what holds Save back until it settles.
            Ui.Input.Bind(() => _model.Title).Label("Title").Id("v6-title"),
            Div.Class("flex gap-2 flex-wrap items-center")[
                Ui.Button.Outline.Id("v6-validate-now").OnClick(ValidateNow)[Ui.Icon.Name(Ui.IconName.Search), "Validate now"],
                Ui.Button.Primary.Submit.Id("v6-submit").Disabled(_ctx.IsValidatingAny)[Ui.Icon.Name(Ui.IconName.CheckCircle), "Save"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Alert.Success.Soft.Class("text-sm mt-3 mb-0")[Ui.Icon.Name(Ui.IconName.CheckCircle), _submission]
    ];
}
