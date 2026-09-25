using Rask.Core.Forms;

namespace Rask.Site.Features;

public sealed partial class AsyncValidationDemo : Component, IDisposable
{
    private readonly EditContext _ctx;
    private readonly SignupModel _model = new();
    private string? _submission;

    public AsyncValidationDemo()
    {
        _ctx = new EditContext(_model);
        _ctx.AddValidator(new UniqueUsernameValidator());
    }

    public void Dispose() => _ctx.Dispose();

    protected override Component? Render() =>
    [
        Form.Model(_model).OnSubmit(m => _submission = $"Signed up: {m.Username}").Context(_ctx).Class("flex flex-col gap-3")[
            // A bound kit field shows "Checking…" while the async validator is out, then the message it
            // records — no Validation.Indicator or Validation.Message to place beside it.
            Ui.Input.Bind(() => _model.Username).Label("Username").Id("v3-username"),
            Div[
                Ui.Button.Tone(Ui.Tone.Primary).Type(Ui.ButtonType.Submit)[Ui.Icon.Name(Ui.IconName.CheckCircle), "Sign up"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Alert.Tone(Ui.Tone.Success).Variant(Ui.Variant.Soft).Class("text-sm mt-3 mb-0")[Ui.Icon.Name(Ui.IconName.CheckCircle), _submission]
    ];
}
