using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Forms;
using Rask.Html.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// Mirrors the Rask.Example.Shared AsyncValidationDemo + ValidationPage structure
// without the showcase layout. Used by the WS dispatcher tests to verify that the
// post-handler render emitted after a per-field IAsyncFieldValidator completes shows
// the validator's terminal message and removes the in-flight indicator.
public sealed partial class AsyncValidationApp : Component
{
    private readonly EditContext _ctx;
    private readonly SignupModel _model = new();

    public AsyncValidationApp()
    {
        // ValidatingStickyMs=0 opts out of the 200ms post-validation sticky tail so
        // this test stays a strict "no indicator after PendingCount drops to 0"
        // assertion — see AsyncFormBindingTests for the same opt-out pattern.
        _ctx = new EditContext(_model) { ValidatingStickyMs = 0 };
        _ctx.AddValidator(new DelayedRejectValidator("admin", "Already taken.", 20));
    }

    protected override Component? HeadAssets => new Title()["async-validation"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Form.Model(_model).Context(_ctx)[
            Input.Bind(() => _model.Username),
            ValidatingIndicator.Template(() => Span.Class("spinner")["Checking..."])
                .For(() => _model.Username),
            ValidationMessage.Template(msgs => Div.Class("text-danger")[msgs[0]])
                .For(() => _model.Username)
        ]
    ];

    public sealed class SignupModel
    {
        public string Username { get; set; } = "";
    }

    private sealed class DelayedRejectValidator(string reject, string message, int delayMs)
        : IAsyncFieldValidator
    {
        public ValueTask ValidateAsync(EditContext context, CancellationToken ct) => ValueTask.CompletedTask;

        public async ValueTask ValidateFieldAsync(
            EditContext context, FieldIdentifier field, CancellationToken ct)
        {
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
            if (context.Model is SignupModel m
                && string.Equals(m.Username, reject, StringComparison.OrdinalIgnoreCase))
            {
                context.AddValidationMessage(field, message);
            }
        }
    }
}
