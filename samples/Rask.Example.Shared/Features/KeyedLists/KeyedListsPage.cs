using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("keyed-lists")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class KeyedListsPage : Component
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

    protected override RenderResult Head => Title()["Keyed lists — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Keyed lists & reconciliation",
            "Give each list item a stable Key: and the diff codec reconciles by identity — inserts, removals, and reorders ship as trusted structural ops that preserve the survivors' DOM state (focus, selection, uncommitted input text) instead of rewriting rows by position."),

        Div(Class: "alert alert-primary d-flex align-items-start")[
            I(Class: "bi bi-lightbulb-fill me-3 fs-4"),
            Div()[
                Strong()["Try it:"],
                " type something into a couple of the inputs below, then press ", Strong()["Rotate"],
                " or ", Strong()["Reverse"], ". With keys ", Strong()["on"],
                ", your typed text travels with its fruit (the row's DOM node is moved). Turn keys ",
                Strong()["off"],
                " and repeat — the labels reorder but the inputs stay put, so your text ends up next to the wrong fruit (positional reconciliation)."
            ]
        ],

        BsCard(Class: Bs.Join(Shadow.Sm, Border.None, Margin.Bottom(4)))[
            BsCardBody()[
                Div(Class: "d-flex flex-wrap align-items-center gap-2 mb-3")[
                    Button(
                        Class: _useKeys ? "btn btn-success btn-sm" : "btn btn-outline-secondary btn-sm",
                        Id: "kl-toggle-keys",
                        OnClick: () => _useKeys = !_useKeys)[
                        I(Class: _useKeys ? "bi bi-key-fill me-1" : "bi bi-key me-1"),
                        _useKeys ? "Keys: ON" : "Keys: OFF"
                    ],
                    Span(Class: "vr mx-1"),
                    Button(Class: "btn btn-outline-primary btn-sm", Id: "kl-rotate", OnClick: Rotate)[
                        I(Class: "bi bi-arrow-down-up me-1"), "Rotate"
                    ],
                    Button(Class: "btn btn-outline-primary btn-sm", Id: "kl-reverse", OnClick: Reverse)[
                        I(Class: "bi bi-arrow-repeat me-1"), "Reverse"
                    ],
                    Button(Class: "btn btn-outline-primary btn-sm", Id: "kl-add", OnClick: AddTop)[
                        I(Class: "bi bi-plus-lg me-1"), "Add to top"
                    ],
                    Button(
                        Class: "btn btn-outline-danger btn-sm",
                        Id: "kl-remove",
                        Disabled: _items.Count == 0,
                        OnClick: RemoveTop)[
                        I(Class: "bi bi-dash-lg me-1"), "Remove top"
                    ]
                ],
                Ul(Class: "list-group", Id: "kl-list")[BuildRows()]
            ]
        ],

        CodeSample(
            ["KeyedListsReorderDemo.cs"],
            Notes:
            "Key is an identity, not a reactive prop: a Key change mounts a fresh instance and never fires OnPropsChanged. On an element Key emits data-rask-key; on a transparent component or Fragment it auto-forwards onto the first rendered element. RASK022 warns when a projected/looped list item is missing a Key."),

        Div(Class: "alert alert-warning d-flex align-items-start mt-3")[
            I(Class: "bi bi-exclamation-triangle-fill me-3 fs-4"),
            Div()[
                Strong()["RASK022:"],
                " a list item produced by ", Code()["Select(...)"], " / ", Code()["SelectMany(...)"],
                " or added to a ", Code()["List<Child>"], " in a loop without a ", Code()["Key:"],
                " raises a build warning. The keyless branch on this page suppresses it on purpose with ",
                Code()["#pragma warning disable RASK022"], " — promote it to an error in your own project with ",
                Code()["<WarningsAsErrors>RASK022</WarningsAsErrors>"], "."
            ]
        ]
    ];

    private List<Child> BuildRows()
    {
        var rows = new List<Child>(_items.Count);
        for (var i = 0; i < _items.Count; i++)
        {
            var f = _items[i];
            // The keyless branch is deliberately unkeyed to demonstrate positional
            // reconciliation; RASK022 would otherwise flag it.
#pragma warning disable RASK022
            rows.Add(_useKeys
                ? Li(Key: f.Id, Class: "list-group-item d-flex align-items-center gap-3")[Row(f, i)]
                : Li(Class: "list-group-item d-flex align-items-center gap-3")[Row(f, i)]);
#pragma warning restore RASK022
        }

        return rows;
    }

    private static List<Child> Row(Fruit f, int index) =>
    [
        Span(Class: "badge bg-secondary rounded-pill")[index + 1],
        Span(Class: "fw-semibold", Style: "min-width: 7rem;")[f.Name],
        Input<string>(
            InputType.Text,
            Class: "form-control form-control-sm kl-note",
            Placeholder: "type here, then reorder…")
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
