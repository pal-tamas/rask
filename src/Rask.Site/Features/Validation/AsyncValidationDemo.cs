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
            // Live, so the name is checked as it is typed. A bound kit field shows "Checking…" while the
            // async validator is out, then the message it records — no Validation.Indicator or
            // Validation.Message to place beside it.
            Ui.Input.Bind(() => _model.Username).Live().Label("Username").Id("v3-username"),
            Div[
                Ui.Button.Primary.Icon(Ui.IconName.CheckCircle).Submit["Sign up"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Class("mt-3").Role("status").Text(_submission)
    ];
}
