namespace Rask.Site.Features;

public sealed partial class ValueObjectValidateDemo : Component
{
    private readonly DestinationModel _model = new();
    private string? _saved;

    protected override Component? Render() =>
    [
        Form.Model(_model).OnSubmit(m => _saved = $"Saved: {m.Name}").Class("flex flex-col gap-3")[
            Ui.Input.Bind(() => _model.Name).Label("Destination")
                .Id("v-vo-name")
                .MaxLength(DestinationName.MaxLength)
                .Validate(DestinationName.Validate),
            Div[
                Ui.Button.Primary.Icon(Ui.IconName.CheckCircle).Submit["Save"]
            ]
        ],
        _saved is null
            ? null
            : Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Class("mt-3").Role("status").Text(_saved)
    ];
}
