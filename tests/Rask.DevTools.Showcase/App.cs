using Rask.Core;
using Rask.Core.Components;
using Rask.Ui;

namespace Rask.DevTools.Showcase;

public sealed partial class App : Component
{
    protected override Component? HeadAssets =>
    [
        Title["Release board"],
        Meta.Charset("utf-8"),
        // Raw, because CSS is not HTML: encoding it would break every selector containing > or &.
        Style[Raw.Value(UiStylesheet.Css)],
    ];

    protected override Component? Render() =>
        UiShell.Theme(UiThemeName.Light)[
            UiTopBar[UiBrand.Label("Release board").Href("#")],
            UiMain[
                Div.Class("grid gap-4 md:grid-cols-2")[
                    TaskBoard.Heading("Ship 1.4").Owner("Ada"),
                    DeployCard.Environment("staging").Region("eu-west").ApiToken("sk_live_do_not_show")
                ]
            ]
        ];
}

/// <summary>A card of tasks; the button adds one, which is a click on the Wire tab and a new row on the Tree tab.</summary>
public sealed partial class TaskBoard : Component
{
    private readonly List<string> _tasks = ["Write the changelog", "Tag the release", "Publish the packages"];

    public string? Heading { get; set; }

    public string? Owner { get; set; }

    protected override Component? Render() =>
        UiCard.Heading(Heading ?? "Tasks")[
            UiList[_tasks.Select((task, i) => TaskRow.Key(task).Done(i == 0).Label(task).Assignee(Owner))],
            UiButton.Tone(UiTone.Primary).OnClick(() => _tasks.Add($"Follow-up {_tasks.Count - 2}"))["Add task"]
        ];
}

public sealed partial class TaskRow : Component
{
    public string? Label { get; set; }

    public bool Done { get; set; }

    public string? Assignee { get; set; }

    protected override Component? Render() =>
        UiListRow
            .Grow(Span[Label ?? string.Empty])
            .Trailing(UiBadge.Tone(Done ? UiTone.Success : UiTone.Info).Variant(UiVariant.Soft)[Done ? "done" : Assignee ?? "open"]);
}

/// <summary>Carries a token, so the Tree tab has a prop to show as redacted.</summary>
public sealed partial class DeployCard : Component
{
    public string? Environment { get; set; }

    public string? Region { get; set; }

    public string? ApiToken { get; set; }

    protected override Component? Render() =>
        UiCard.Heading("Deploy")[
            P[$"Target: {Environment} ({Region})"],
            P[ApiToken is null ? "No token configured." : "Token configured."]
        ];
}
