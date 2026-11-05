namespace Rask.Site.Features.UiDocs.Examples;

// Delete asks in the layout's Ui.Confirm, Cancel goes back to the list, Save submits the form.
public sealed partial class FormButtonsExample : Component
{
    private readonly Supplier _supplier = new();
    private string _said = "Nothing saved yet.";

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Form.Model(_supplier).OnSubmit(Save).Id("ufb-form").Class("max-w-md space-y-6")[
                Ui.Input.Bind(() => _supplier.Name).Label("Name"),
                Ui.Input.Bind(() => _supplier.City).Label("City"),
                Ui.FormButtons.Back(Routes.UiRowActionsPage()).Remove(Delete, "Delete supplier", $"Delete {_supplier.Name}? This cannot be undone.")
            ],
            Ui.Text.Id("ufb-said")[_said]
        ];

    private void Save(Supplier supplier) => _said = $"Saved {supplier.Name}, {supplier.City}.";

    private void Delete() => _said = $"Deleted {_supplier.Name}.";

    public sealed class Supplier
    {
        public string Name { get; set; } = "Aminoff Freight";

        public string City { get; set; } = "Oslo";
    }
}
