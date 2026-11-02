using System.Globalization;

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
                Ui.Button.Icon(Ui.IconName.Key)
                    .Variant(_useKeys ? Ui.ButtonVariant.Primary : Ui.ButtonVariant.Outline)
                    .Color(_useKeys ? Ui.Color.Green : null)
                    .Id("kl-toggle-keys")
                    .OnClick(() => _useKeys = !_useKeys)[_useKeys ? "Keys: ON" : "Keys: OFF"],
                Span.Class("vr mx-1"),
                Ui.Button.Icon(Ui.IconName.ArrowsUpDown).Id("kl-rotate").OnClick(Rotate)["Rotate"],
                Ui.Button.Icon(Ui.IconName.ArrowPath).Id("kl-reverse").OnClick(Reverse)["Reverse"],
                Ui.Button.Icon(Ui.IconName.Plus).Id("kl-add").OnClick(AddTop)["Add to top"],
                Ui.Button.Red.Icon(Ui.IconName.Minus)
                    .Id("kl-remove")
                    .Disabled(_items.Count == 0)
                    .OnClick(RemoveTop)["Remove top"]
            ],
            Ui.Table.Id("kl-list")[
                Ui.TableColumns[Ui.TableColumn["#"], Ui.TableColumn["Fruit"], Ui.TableColumn["Note"]],
                Ui.TableRows[BuildRows()]
            ]
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
                ? Ui.TableRow.Key(f.Id)[Row(f, i)]
                : Ui.TableRow[Row(f, i)]);
#pragma warning restore RASK022
        }

        return rows;
    }

    private static List<Component> Row(Fruit f, int index) =>
    [
        Ui.TableCell[Ui.Badge[(index + 1).ToString(CultureInfo.InvariantCulture)]],
        Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Class("kl-name")[f.Name],
        // The column says "Note" to the eye; the label around the field says it to a screen reader.
        Ui.TableCell[
            Label[
                Span.Class("sr-only")[$"Note for {f.Name}"],
                Ui.Input.Of<string>()
                    .Type(InputType.Text)
                    .Placeholder("type here, then reorder…").Class("kl-note")
            ]
        ]
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
