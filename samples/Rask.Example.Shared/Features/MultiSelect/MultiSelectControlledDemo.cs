namespace Rask.Example.Shared.Features;

// Controlled MultiSelect<TItem>: no Bind / EditContext. The parent owns the selection in its own field and
// the control reports each change through OnChange (an OnChangeAsync variant also exists). Passing Value +
// OnChange instead of Bind is the React-style controlled shape — useful when the selection is not a form
// model property. The control still re-renders its host automatically, so the "Selected:" summary updates
// without any StateHasChanged.
public sealed class MultiSelectControlledDemo : Component
{
    private static readonly string[] AllTopics =
        ["News", "Sports", "Tech", "Music", "Travel"];

    private IReadOnlyCollection<string> _topics = [];

    protected override RenderResult Render() =>
        Div(Class: "vstack gap-3")[
            Div()[
                Label(Class: "form-label fw-semibold")["Topics"],
                MultiSelect<string>(
                    AllTopics,
                    Value: _topics.ToList(),
                    OnChange: next => _topics = next,
                    Id: "ms-controlled",
                    Placeholder: "Choose topics…")
            ],
            P(Class: "small text-secondary mb-0", Id: "ms-controlled-summary")[
                "Selected: " + (_topics.Count == 0 ? "none" : string.Join(", ", _topics))
            ]
        ];
}
