namespace Rask.Site.Features;

// An async DataAnnotations attribute on the model, and nothing else: the form awaits it by itself. The kit field
// says what it found, the button waits while the lookup is out, and a value changed again drops the old lookup.
public sealed partial class AsyncAttributeDemo : Component
{
    private readonly NewsletterModel _model = new();
    private string? _submission;

    protected override Component? Render() =>
    [
        Form.Model(_model).OnSubmit(m => _submission = $"Subscribed: {m.Email}").Class("flex flex-col gap-3")[
            Ui.Input.Bind(() => _model.Email).Blur().Label("Email").Id("v15-email").Type(InputType.Email),
            Div[
                Ui.Button.Primary.Icon(Ui.IconName.Envelope).Submit["Subscribe"]
            ]
        ],
        _submission is null
            ? null
            : Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Class("mt-3").Role("status").Text(_submission)
    ];
}
