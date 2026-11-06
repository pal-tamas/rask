using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs.Examples;

// A form with rows of its own opens as a sheet from the right, as wide as the screen allows. Its own key, so it opens
// alone on a page that holds another editor.
public sealed partial class EditorDialogWideExample(RouteState route) : Component
{
    private readonly Delivery _delivery = new();

    protected override Component? Render() =>
        Div[
            Ui.Button.Key("new-delivery").Sm.Icon(Ui.IconName.Plus).Href(route.With("delivery", UiEditorDialog.New))["New delivery"],
            Ui.EditorDialog.In("delivery").Wide().Heading("New delivery")[
                Form.Model(_delivery).OnSubmit(_ => Go.Without("delivery")).Id("ued-delivery").Class("space-y-6")[
                    Div.Class("grid gap-4 md:grid-cols-4")[
                        Ui.Input.Bind(() => _delivery.Vehicle).Label("Vehicle"),
                        Ui.Input.Bind(() => _delivery.Driver).Label("Driver"),
                        Ui.Input.Bind(() => _delivery.From).Label("From"),
                        Ui.Input.Bind(() => _delivery.To).Label("To")
                    ],
                    Ui.Textarea.Bind(() => _delivery.Note).Label("Note"),
                    Ui.FormButtons.Back(route.With("delivery", null)).Save("Create")
                ]
            ]
        ];

    public sealed class Delivery
    {
        public string Vehicle { get; set; } = "ABC-123";

        public string Driver { get; set; } = "Ada Lovelace";

        public string From { get; set; } = "Oslo";

        public string To { get; set; } = "Leeds";

        public string Note { get; set; } = "";
    }
}
