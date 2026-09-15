using Rask.Core;
using Rask.Core.Components;
using Rask.Ui;

namespace Rask.DevTools.E2E.Tests.Apps;

/// <summary>
///     The page the Server journeys inspect: a board whose button adds a row, rows that read the release the board
///     provides, and a card with a secret prop and a button that throws.
/// </summary>
public sealed partial class BoardApp : Component
{
    protected override Component? HeadAssets =>
    [
        Title["DevTools E2E board"],
        Meta.Charset("utf-8"),
        // Raw, because CSS is not HTML: encoding it would break every selector containing > or &.
        Style[Raw.Value(UiStylesheet.Css)],
    ];

    protected override Component? Render() =>
        UiShell.Theme(UiThemeName.Light)[
            UiMain[
                Board.Heading("Ship 1.4"),
                DeployCard.Environment("staging").ApiToken("sk_live_do_not_show")
            ]
        ];
}

/// <summary>What <see cref="Board" /> provides to its rows.</summary>
public sealed record Release(string Name);

public sealed partial class Board : Component
{
    private readonly List<string> _tasks = ["Write the changelog", "Tag the release"];

    public string? Heading { get; set; }

    protected override Component? Render() =>
        Context.Provide(new Release(Heading ?? "Tasks"))[
            Section.Id("board")[
                H2[Heading ?? "Tasks"],
                Ul[_tasks.Select((task, i) => BoardRow.Key(task).Done(i == 0).Label(task))],
                Button.Id("add-task").OnClick(() => _tasks.Add($"Follow-up {_tasks.Count - 1}"))["Add task"]
            ]
        ];
}

public sealed partial class BoardRow : Component
{
    public string? Label { get; set; }

    public bool Done { get; set; }

    protected override Component? Render()
    {
        var release = Context.Get<Release>();
        return Li.Class("board-row").Title(release?.Name)[Label ?? string.Empty, Done ? " (done)" : null];
    }
}

/// <summary>A secret prop the panel must not show, and a handler that fails.</summary>
public sealed partial class DeployCard : Component
{
    public string? Environment { get; set; }

    public string? ApiToken { get; set; }

    protected override Component? Render() =>
        Section.Id("deploy")[
            P[$"Target: {Environment}"],
            Button.Id("deploy-button").OnClick(Deploy)["Deploy"]
        ];

    private void Deploy() =>
        throw new InvalidOperationException($"The {Environment} environment is locked by another deploy.");
}
