namespace Rask.Site.Features.UiDocs.Examples;

// Press Save with a field empty: each tab counts its own in red, and the first such tab opens.
public sealed partial class FormTabsExample : Component
{
    private readonly Supplier _supplier = new();
    private string _said = "Nothing saved yet.";

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Form.Model(_supplier).OnSubmit(Save).Id("uft-form").Class("max-w-md space-y-6")[
                Ui.FormTabs[
                    Ui.FormTab.Name("details").Label("Details").Holds(() => _supplier.Name, () => _supplier.Email).Class("space-y-6")[
                        Ui.Input.Bind(() => _supplier.Name).Label("Name").Id("uft-name").Validate(Filled),
                        Ui.Input.Bind(() => _supplier.Email).Label("Email").Id("uft-email").Validate(Filled)
                    ],
                    Ui.FormTab.Name("address").Label("Address").Holds(() => _supplier.Address).Class("space-y-6")[
                        Ui.Input.Bind(() => _supplier.Address.Street).Label("Street").Id("uft-street").Validate(Filled),
                        Ui.Input.Bind(() => _supplier.Address.City).Label("City").Id("uft-city").Validate(Filled)
                    ],
                    Ui.FormTab.Name("notes").Label("Notes")[
                        Ui.Textarea.Bind(() => _supplier.Notes).Label("Notes").Id("uft-notes")
                    ]
                ],
                Ui.Button.Submit.Primary.Id("uft-save")["Save"]
            ],
            Ui.Text.Id("uft-said")[_said]
        ];

    private void Save(Supplier supplier) => _said = $"Saved {supplier.Name}, {supplier.Address.City}.";

    private static IEnumerable<string> Filled(string? value) => string.IsNullOrWhiteSpace(value) ? ["Fill this in."] : [];

    public sealed class Supplier
    {
        public string Name { get; set; } = "Aminoff Freight";

        public string Email { get; set; } = "";

        public Place Address { get; set; } = new();

        public string Notes { get; set; } = "";
    }

    public sealed class Place
    {
        public string Street { get; set; } = "";

        public string City { get; set; } = "";
    }
}
