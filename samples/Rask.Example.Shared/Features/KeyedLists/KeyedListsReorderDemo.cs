namespace Rask.Example.Shared.Features;

// Keyed reconciliation in miniature. A stable Key: per row makes a reorder ship trusted Move ops, so
// each row's DOM node — and any uncommitted input value living only in the DOM — follows its logical row
// instead of being rewritten by position. Toggle Keys OFF to see positional reconciliation instead: the
// labels reorder but the inputs stay put, so typed text ends up next to the wrong fruit.
public sealed partial class KeyedListsReorderDemo : Component
{
    private readonly List<Fruit> _items =
    [
        new(1, "Apple"),
        new(2, "Banana"),
        new(3, "Cherry"),
        new(4, "Date"),
        new(5, "Elderberry")
    ];

    private int _nextId = 6;

    private bool _useKeys = true;

    protected override Component? Render() =>
        Div[
            Div.Class("flex gap-2 items-center flex-wrap mb-3")[
                Button
                    .Class(_useKeys ? $"{Tw.BtnSuccess}" : $"{Tw.BtnOutlineSecondary}")
                    .Id("kl-toggle-keys")
                    .OnClick(() => _useKeys = !_useKeys)[
                    Icon.Name(_useKeys ? IconName.KeyFill : IconName.Key).Class("me-1"),
                    _useKeys ? "Keys: ON" : "Keys: OFF"
                ],
                Span.Class("vr mx-1"),
                Button.Type("button").Class(Tw.BtnOutlinePrimary).Id("kl-rotate").OnClick(Rotate)[
                    Icon.Name(IconName.ArrowDownUp).Class("me-1"), "Rotate"
                ],
                Button.Type("button").Class(Tw.BtnOutlinePrimary).Id("kl-reverse").OnClick(Reverse)[
                    Icon.Name(IconName.ArrowRepeat).Class("me-1"), "Reverse"
                ],
                Button.Type("button").Class(Tw.BtnOutlinePrimary).Id("kl-add").OnClick(AddTop)[
                    Icon.Name(IconName.PlusLg).Class("me-1"), "Add to top"
                ],
                Button.Type("button").Class(Tw.BtnOutlineDanger)
                    .Id("kl-remove")
                    .Disabled(_items.Count == 0)
                    .OnClick(RemoveTop)[
                    Icon.Name(IconName.DashLg).Class("me-1"), "Remove top"
                ]
            ],
            Ul.Class(Tw.ListGroup).Id("kl-list")[BuildRows()]
        ];

    private List<Component> BuildRows()
    {
        var rows = new List<Component>(_items.Count);
        for (var i = 0; i < _items.Count; i++)
        {
            var f = _items[i];
            // The keyless branch is deliberately unkeyed to demonstrate positional
            // reconciliation; RASK022 would otherwise flag it.
#pragma warning disable RASK022
            rows.Add(_useKeys
                ? Li.Key(f.Id).Class($"{Tw.ListGroupItem} flex items-center gap-3")[Row(f, i)]
                : Li.Class($"{Tw.ListGroupItem} flex items-center gap-3")[Row(f, i)]);
#pragma warning restore RASK022
        }

        return rows;
    }

    private static List<Component> Row(Fruit f, int index) =>
    [
        Span.Class(Tw.BadgeSecondary)[index + 1],
        Span.Class("font-semibold").Style("min-width: 7rem;")[f.Name],
        Input.Value<string>(null)
            .Type(InputType.Text)
            .Class($"{Tw.Input} kl-note")
            .Placeholder("type here, then reorder…")
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

    private void Reverse() => _items.Reverse();

    private void AddTop()
    {
        _items.Insert(0, new Fruit(_nextId, $"Fruit {_nextId}"));
        _nextId++;
    }

    private void RemoveTop()
    {
        if (_items.Count > 0)
        {
            _items.RemoveAt(0);
        }
    }

    private sealed record Fruit(int Id, string Name);
}
