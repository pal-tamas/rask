namespace Rask.Site.Features.UiDocs.Examples;

// A page that decides for itself who may see it draws Ui.Forbidden in place of its content.
public sealed partial class ForbiddenExample : Component
{
    private bool _admin;

    protected override Component? Render() =>
        Div.Class("space-y-4")[
            Ui.Field.Variant(Ui.FieldVariant.Inline)[
                Ui.Label["Signed in as an administrator"],
                Ui.Switch.Value(_admin).OnChange(on => { _admin = on; }).Id("uf-admin"),
                Ui.Error
            ],
            _admin ? Ui.Text.Id("uf-records")["Three suppliers, 412 deliveries."] : Ui.Forbidden
        ];
}
