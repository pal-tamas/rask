namespace Rask.Site.Features;

public sealed partial class RefusedSaveDemo : Component
{
    private readonly HashSet<string> _routes = new(StringComparer.OrdinalIgnoreCase) { "Budapest – Wien" };
    private readonly RouteModel _model = new();
    private string? _saved;

    protected override Component? Render() =>
    [
        Form.Model(_model).OnSubmit(Save).Class("flex flex-col gap-3")[
            Ui.Input.Bind(() => _model.Name).Label("Route").Description("Budapest – Wien is already saved.")
                .Id("v13-name")
                .Validate(name => string.IsNullOrWhiteSpace(name) ? ["A route needs a name."] : []),
            Div[
                Ui.Button.Primary.Icon(Ui.IconName.CheckCircle).Submit["Save"]
            ]
        ],
        _saved is null
            ? null
            : Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Class("mt-3").Role("status").Text(_saved)
    ];

    // The store's part: a unique index on the name refuses the second one as it is written.
    private async Task Save(RouteModel route)
    {
        await Task.Delay(150);
        if (!_routes.Add(route.Name))
        {
            throw new RouteNameTakenException();
        }

        _saved = $"Saved: {route.Name}";
    }
}
