namespace Rask.Site.Features.UiDocs.Examples;

// A new record has nothing to delete, and its submit creates it.
public sealed partial class FormButtonsWordsExample : Component
{
    private readonly Supplier _supplier = new();
    private string _said = "Nothing created yet.";

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Form.Model(_supplier).OnSubmit(Create).Id("ufbw-form").Class("max-w-md space-y-6")[
                Ui.Input.Bind(() => _supplier.Name).Label("Name"),
                Ui.FormButtons.Back(Routes.UiRowActionsPage()).Save("Create").Cancel("Back")
            ],
            Ui.Text.Id("ufbw-said")[_said]
        ];

    private void Create(Supplier supplier) => _said = $"Created {supplier.Name}.";

    public sealed class Supplier
    {
        public string Name { get; set; } = "Quist Depot";
    }
}
