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

/// <summary>
///     A card of tasks; the button adds one, which is a click on the Wire tab and a new row on the Tree tab. It provides the
///     release its rows belong to, so the Tree tab has context to show.
/// </summary>
public sealed partial class TaskBoard : Component
{
    private readonly List<string> _tasks = ["Write the changelog", "Tag the release", "Publish the packages"];

    public string? Heading { get; set; }

    public string? Owner { get; set; }

    protected override Component? Render() =>
        Context.Provide(new Release(Heading ?? "Tasks"))[
            UiCard.Heading(Heading ?? "Tasks")[
                UiList[_tasks.Select((task, i) => TaskRow.Key(task).Done(i == 0).Label(task).Assignee(Owner))],
                UiButton.Tone(UiTone.Primary).OnClick(() => _tasks.Add($"Follow-up {_tasks.Count - 2}"))["Add task"]
            ]
        ];
}

/// <summary>The release a board's tasks belong to.</summary>
public sealed record Release(string Name);

public sealed partial class TaskRow : Component
{
    public string? Label { get; set; }

    public bool Done { get; set; }

    public string? Assignee { get; set; }

    protected override Component? Render()
    {
        var release = Context.Get<Release>();
        return UiListRow
            .Grow(Span[Label ?? string.Empty])
            .Trailing(UiBadge.Tone(Done ? UiTone.Success : UiTone.Info).Variant(UiVariant.Soft)
                .Title(release is null ? null : "Part of " + release.Name)[Done ? "done" : Assignee ?? "open"]);
    }
}

/// <summary>
///     Carries a token, so the Tree tab has a prop to show as redacted; and a Deploy button whose handler throws, so the
///     Errors tab has something to list.
/// </summary>
public sealed partial class DeployCard : Component
{
    public string? Environment { get; set; }

    public string? Region { get; set; }

    public string? ApiToken { get; set; }

    protected override Component? Render() =>
        UiCard.Heading("Deploy")[
            P[$"Target: {Environment} ({Region})"],
            P[ApiToken is null ? "No token configured." : "Token configured."],
            UiButton.Title("Throws, so the devtools have an error to show").OnClick(Deploy)["Deploy"]
        ];

    private void Deploy() =>
        throw new InvalidOperationException($"The {Environment} environment is locked by another deploy.");
}
