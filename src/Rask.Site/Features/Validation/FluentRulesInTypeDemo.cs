namespace Rask.Site.Features;

public sealed partial class FluentRulesInTypeDemo : Component
{
    private readonly SupplierModel _model = new();
    private string? _saved;

    protected override Component? Render() =>
    [
        Form.Model(_model).OnSubmit(m => _saved = $"Saved {m.Name}").Id("v47-supplier").Class("flex flex-col gap-3")[
            Ui.Field.Bind(() => _model.Name).Label("Name"),
            Ui.Field.Bind(() => _model.Email).Label("E-mail"),
            Ui.Field.Bind(() => _model.Rating).Label("Rating"),
            Div[
                Ui.Button.Primary.Submit["Save supplier"]
            ]
        ],
        _saved is null
            ? null
            : Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Class("mt-3").Role("status").Text(_saved)
    ];
}
