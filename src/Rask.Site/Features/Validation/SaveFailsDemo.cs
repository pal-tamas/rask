namespace Rask.Site.Features;

public sealed partial class SaveFailsDemo : Component
{
    private readonly RouteModel _model = new() { Name = "Wien – Graz" };

    protected override Component? Render() =>
        Form.Model(_model).OnSubmit(Save).Class("flex flex-col gap-3")[
            Ui.Input.Bind(() => _model.Name).Label("Route").Id("v14-name"),
            Div[
                Ui.Button.Primary.Icon(Ui.IconName.CheckCircle).Submit.Id("v14-save")["Save"]
            ]
        ];

    // The store is down. Nothing here catches that, and nothing here tells the reader: the form does.
    private static async Task Save(RouteModel route)
    {
        await Task.Delay(150);
        throw new TimeoutException("The database did not answer within 30 seconds.");
    }
}
