namespace Rask.Site.Features;

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
                UiButton
                    .Label(_useKeys ? "Keys: ON" : "Keys: OFF")
                    .Icon(UiIconName.Key)
                    .Tone(_useKeys ? UiTone.Success : null)
                    .Variant(_useKeys ? null : UiVariant.Outline)
                    .Id("kl-toggle-keys")
                    .OnClick(() => _useKeys = !_useKeys),
                Span.Class("vr mx-1"),
                UiButton.Label("Rotate").Icon(UiIconName.ArrowsUpDown).Tone(UiTone.Primary).Variant(UiVariant.Outline).Id("kl-rotate").OnClick(Rotate),
                UiButton.Label("Reverse").Icon(UiIconName.Retry).Tone(UiTone.Primary).Variant(UiVariant.Outline).Id("kl-reverse").OnClick(Reverse),
                UiButton.Label("Add to top").Icon(UiIconName.Plus).Tone(UiTone.Primary).Variant(UiVariant.Outline).Id("kl-add").OnClick(AddTop),
                UiButton.Label("Remove top").Icon(UiIconName.Minus).Tone(UiTone.Error).Variant(UiVariant.Outline)
                    .Id("kl-remove")
                    .Disabled(_items.Count == 0)
                    .OnClick(RemoveTop)
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
