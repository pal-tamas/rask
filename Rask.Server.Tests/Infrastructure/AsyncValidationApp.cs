using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Forms;

namespace Rask.Server.Tests.Infrastructure;

// Mirrors the Rask.Example.Shared AsyncValidationDemo + ValidationPage structure
// without the showcase layout. Used by the WS dispatcher tests to verify that the
// post-handler render emitted after a per-field IAsyncFieldValidator completes shows
// the validator's terminal message and removes the in-flight indicator.
public sealed class AsyncValidationApp : Component
{
    private readonly SignupModel _model = new();
    private readonly EditContext _ctx;

    public AsyncValidationApp()
    {
        _ctx = new EditContext(_model);
        _ctx.AddValidator(new DelayedRejectValidator("admin", "Already taken.", 20));
    }

    protected override Component Render() =>
        Fragment()[
            Doctype(),
            new Html()[
                new Head()[new Title()["async-validation"]],
                new Body()[
                    Form<SignupModel>(_model, Context: _ctx)[
                        Input(() => _model.Username),
                        ValidatingIndicator(
                            () => _model.Username,
                            () => Span(Class: "spinner")["Checking..."]),
                        ValidationMessage(
                            () => _model.Username,
                            msgs => Div(Class: "text-danger")[msgs[0]])
                    ]
                ]
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
