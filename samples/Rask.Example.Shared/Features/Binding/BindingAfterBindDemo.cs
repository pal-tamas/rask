namespace Rask.Example.Shared.Features;

public sealed class BindingAfterBindDemo : Component
{
    private static readonly Dictionary<string, string[]> Cities = new()
    {
        ["US"] = new[] { "New York", "Los Angeles", "Chicago" },
        ["DE"] = new[] { "Berlin", "Hamburg", "Munich" },
        ["JP"] = new[] { "Tokyo", "Osaka", "Kyoto" }
    };

    private readonly Holder _model = new();
    private string[] _cities = Cities["US"];

    protected override Component? Render() =>
    [
        Div(Class: "mb-3")[
            Label("bind-after-country", Class: "form-label small")["Country"],
            Select(
                () => _model.Country,
                c =>
                {
                    _cities = Cities[c];
                    _model.City = _cities[0];
                },
                Id: "bind-after-country",
                Class: "form-select")[
                Option("US")["United States"],
                Option("DE")["Germany"],
                Option("JP")["Japan"]
            ]
        ],
        Div(Class: "mb-3")[
            Label("bind-after-city", Class: "form-label small")["City"],
            Select(
                () => _model.City,
                Id: "bind-after-city",
                Class: "form-select")[
                _cities.Select(c => Option(c, Key: c)[c])
            ]
        ],
        Pre(Class: "small mb-0 p-3 bg-light border rounded")[
            Code("bind-after-echo")[
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
