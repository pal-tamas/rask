namespace Rask.Example.Shared.Features;

// Keyed reconciliation in miniature. A stable Key: per row makes a reorder ship trusted
// Move ops, so each row's DOM node — and any uncommitted input value living only in the
// DOM — follows its logical row instead of being rewritten by position.
public sealed class KeyedListsReorderDemo : Component
{
    private readonly List<Fruit> _items =
    [
        new(1, "Apple"),
        new(2, "Banana"),
        new(3, "Cherry")
    ];

    protected override RenderResult Render() =>
        Div()[
            // Keyed: a stable Key: per row. Reorders ship trusted Move ops, so the DOM
            // node (and its focus / uncommitted input value) follows its logical row.
            Ul(Class: "list-group")[
                _items.Select(f => Li(Key: f.Id, Class: "list-group-item")[
                    Span()[f.Name],
                    Input("text") // unbound — its value lives only in the DOM
                ])
            ],
            Button(Class: "btn btn-sm btn-outline-primary mt-2", OnClick: Rotate)["Rotate"]
        ];

    private void Rotate()
    {
        if (_items.Count < 2)
        {
            return;
        }

        var first = _items[0];
        _items.RemoveAt(0);
        _items.Add(first);
    }

    private sealed record Fruit(int Id, string Name);
}
