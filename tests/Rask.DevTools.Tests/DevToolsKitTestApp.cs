using Rask;
using Rask.Core;

namespace Rask.DevTools.Tests;

/// <summary>
///     An app page built from the kit's own components, the way a real app is: a shell, a bar, a card of rows and a button
///     with a handler (so the page stays live). What the tree tests check the snapshot against when a page is not just
///     plain elements.
/// </summary>
public sealed partial class DevToolsKitTestApp : Component
{
    private readonly List<string> _rows = ["one", "two"];

    protected override Component? Render() =>
        Ui.Shell.Theme(Ui.ThemeName.Light)[
            Ui.TopBar[Ui.Brand.Label("Kit").Href("#")],
            Ui.Main[
                DevToolsKitBoard.Heading("Board")[
                    Ui.List[_rows.Select(r => DevToolsKitRow.Key(r).Done(r == "one").Label(r))],
                    Ui.Button.OnClick(() => _rows.Add("more"))["Add"]
                ]
            ]
        ];
}

/// <summary>A component that renders what it is given inside a kit card.</summary>
public sealed partial class DevToolsKitBoard : Component
{
    public string? Heading { get; set; }

    protected override Component? Render() => Ui.Card.Heading(Heading ?? "")[Children ?? []];
}

/// <summary>One row of the board.</summary>
public sealed partial class DevToolsKitRow : Component
{
    public string? Label { get; set; }

    public bool Done { get; set; }

    protected override Component? Render() =>
        Ui.ListRow.Grow(Span[Label ?? ""]).Trailing(Ui.Badge[Done ? "done" : "open"]);
}
