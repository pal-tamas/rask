namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PhoneBoundExample : Component
{
    private readonly Contact _contact = new();
    private string? _saved;

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Phone.Bind(() => _contact.Phone).Country("US").Placeholder("(201) 555-0123").Id("ui-phone-bound"),
            Div.Class("flex items-center gap-3")[
                Ui.Button.Sm.Data("testid", "ui-phone-save").OnClick(() => { _saved = _contact.Phone ?? ""; })["Save"],
                Ui.Text.Data("testid", "ui-phone-saved")[Saved()]
            ]
        ];

    private string Saved() => _saved switch
    {
        null => "Nothing saved yet.",
        "" => "Saved: (empty)",
        var number => "Saved: " + number,
    };

    public sealed class Contact
    {
        public string? Phone { get; set; }
    }
}
