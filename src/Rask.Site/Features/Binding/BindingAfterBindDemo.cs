namespace Rask.Site.Features;

public sealed partial class BindingAfterBindDemo : Component
{
    private static readonly Dictionary<string, string[]> Cities = new()
    {
        ["US"] = new[] { "New York", "Los Angeles", "Chicago" },
        ["DE"] = new[] { "Berlin", "Hamburg", "Munich" },
        ["JP"] = new[] { "Tokyo", "Osaka", "Kyoto" }
    };

    private static readonly (string Value, string Text)[] Countries =
        [("US", "United States"), ("DE", "Germany"), ("JP", "Japan")];

    private readonly Holder _model = new();
    private string[] _cities = Cities["US"];

    protected override Component? Render() =>
    [
        Div.Class("mb-3")[
            UiSelect.Bind(() => _model.Country)
                .Options(Countries)
                .Label("Country")
                .AfterBind(c =>
                {
                    _cities = Cities[c];
                    _model.City = _cities[0];
                })
                .Id("bind-after-country")
        ],
        Div.Class("mb-3")[
            UiSelect.Bind(() => _model.City)
                .Options([.. _cities.Select(c => (c, c))])
                .Label("City")
                .Id("bind-after-city")
        ],
        Pre.Class("text-sm mb-0 p-3 bg-ui-well border rounded")[
            Code.Id("bind-after-echo")[
                $"Country = {_model.Country}\n" +
                $"City    = {_model.City}"
            ]
        ]
    ];

    private sealed class Holder
    {
        public string Country { get; set; } = "US";
        public string City { get; set; } = "New York";
    }
}
