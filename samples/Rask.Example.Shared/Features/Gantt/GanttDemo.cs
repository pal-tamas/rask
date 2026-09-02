namespace Rask.Example.Shared.Features;

// Wrapping a third-party JS library (frappe-gantt) so it behaves like any other Rask component: props go
// in, events come back as plain C# delegates, and the state below the chart is ordinary C# that
// re-renders itself. Drag or resize a bar and the table updates — the round trip is
// browser -> [JSInvokable] -> this component's state -> live re-render, with no StateHasChanged anywhere
// on the way.
//
// The wrapper is Gantt.cs + Gantt.js; this file is just a caller.
public sealed partial class GanttDemo : Component
{
    private static readonly DateOnly Start = new(2026, 3, 2);

    private static readonly GanttHoliday[] Holidays = [new(Start.AddDays(10), "Company offsite")];

    // Replaced, never mutated in place. Props are diffed with EqualityComparer<T>.Default, which for a
    // list means reference equality — mutate the same instance and the diff (correctly) reports no
    // change, OnPropsChanged never fires, and the chart silently stops tracking the data.
    private IReadOnlyList<GanttTask> _tasks =
    [
        new("design", "Design system", Start, Start.AddDays(6), 100),
        new("api", "Public API", Start.AddDays(4), Start.AddDays(13), 60),
        new("docs", "Guides & docs", Start.AddDays(9), Start.AddDays(18), 25),
        new("ship", "Ship 1.0", Start.AddDays(16), Start.AddDays(20), 0)
    ];

    // Newest first, capped — a log rather than a single "last event" line because the library fires more
    // than one event per gesture (a drag ends in a native click, so a move reports both).
    private readonly List<(int Seq, string Text)> _log = [];

    private int _added;
    private int _seq;
    private GanttViewMode _viewMode = GanttViewMode.Day;

    protected override Component? Render() =>
        Div.Class("flex flex-col gap-3")[
            Div.Class("flex gap-2 items-center flex-wrap items-center")[
                // A segmented control: the selected mode carries the filled style, the rest the outline
                // one. BsButtonGroup's .Active() was a prop; here it is just which class string wins.
                Div.Class("inline-flex gap-1")[
                    Enum.GetValues<GanttViewMode>().Select(mode =>
                        Button
                            .Key(mode.ToString())
                            .Type("button")
                            .Class(_viewMode == mode ? Tw.BtnPrimary : Tw.BtnOutlineSecondary)
                            .OnClick(() => _viewMode = mode)[ViewModeLabel(mode)])
                ],
                // Add/remove push a new task list at the library — the prop-change path.
                Button.Type("button").Class(Tw.BtnOutlinePrimary).OnClick(AddTask)[
                    Icon.Name(IconName.Plus), " Add task"],
                // Disabled at one task rather than rendered conditionally: a component's identity is its
                // (type, position) among its parent's children, so a sibling that disappears shifts every
                // later child's position by one. The Gantt below would be matched against the wrong slot
                // and rebuilt from scratch — remounting the chart on an unrelated click.
                Button.Type("button").Class(Tw.BtnOutlineDanger)
                    .Disabled(_tasks.Count <= 1)
                    .OnClick(RemoveLast)["Remove last"]
            ],

            // Props in, delegates out — the wrapper's whole surface. A new _tasks reference is what makes
            // the prop diff fire OnPropsChanged, which is what pushes the new bars at the library.
            Gantt
                .Data(_tasks)
                .Holidays(Holidays)
                .ViewMode(_viewMode)
                .OnTaskClick(TaskClicked)
                .OnDateChange(TaskMoved)
                .OnProgressChange(ProgressChanged),

            Div.Class($"{Tw.Card} gantt-log")[
                Div.Class($"{Tw.CardBody} py-2")[
                    Div.Class("text-sm text-slate-500 dark:text-slate-400 mb-1")["Events from the chart:"],
                    _log.Count == 0
                        ? Div.Class("text-sm italic text-slate-500 dark:text-slate-400")[
                            "Nothing yet — click a bar, or drag one to move it."]
                        : Ul.Class("list-unstyled text-sm mb-0 font-mono")[
                            _log.Select(e => Li.Key(e.Seq.ToString())[e.Text])]
                ]
            ],

            // The C# state, rendered normally. This is what proves the round trip: it can only change
            // because the browser pushed an event into the callbacks above.
            Table.Class("w-full border-collapse text-left text-sm")[
                Thead[Tr[Th["Task"], Th["Start"], Th["End"], Th.Class("text-right")["Progress"]]],
                Tbody[
                    _tasks.Select(t =>
                        Tr.Key(t.Id)[
                            Td[t.Name],
                            Td.Class("font-mono text-sm")[t.Start.ToString("yyyy-MM-dd")],
                            Td.Class("font-mono text-sm")[t.End.ToString("yyyy-MM-dd")],
                            Td.Class("text-right")[Span.Class(Tw.BadgeSecondary)[$"{t.Progress:F0}%"]]
                        ])
                ]
            ]
        ];

    private static string ViewModeLabel(GanttViewMode mode) => mode switch
    {
        GanttViewMode.QuarterDay => "¼ day",
        GanttViewMode.HalfDay => "½ day",
        _ => mode.ToString()
    };

    private static string ProgressColor(double progress) => progress switch
    {
        >= 100 => "bg-emerald-500",
        > 0 => "bg-sky-500",
        _ => "bg-slate-400"
    };

    // No StateHasChanged anywhere below: each of these is a single-argument callback prop, so the
    // generated factory wraps it and re-renders this component when it fires — the same path an OnClick
    // takes. That is the whole reason GanttDateChange/GanttProgressChange bundle their arguments into a
    // record instead of taking two or three.
    private Task TaskClicked(string id)
    {
        Log($"click: {Name(id)}");
        return Task.CompletedTask;
    }

    private Task TaskMoved(GanttDateChange e)
    {
        Replace(e.TaskId, t => t with { Start = e.Start, End = e.End });
        Log($"date_change: {Name(e.TaskId)} → {e.Start:yyyy-MM-dd} – {e.End:yyyy-MM-dd}");
        return Task.CompletedTask;
    }

    private Task ProgressChanged(GanttProgressChange e)
    {
        Replace(e.TaskId, t => t with { Progress = e.Progress });
        Log($"progress_change: {Name(e.TaskId)} → {e.Progress:F0}%");
        return Task.CompletedTask;
    }

    private void Log(string text)
    {
        _log.Insert(0, (++_seq, text));
        if (_log.Count > 5)
        {
            _log.RemoveRange(5, _log.Count - 5);
        }
    }

    private string Name(string id) => Find(id)?.Name ?? id;

    private GanttTask? Find(string id) => _tasks.FirstOrDefault(t => t.Id == id);

    private void Replace(string id, Func<GanttTask, GanttTask> update) =>
        _tasks = [.. _tasks.Select(t => t.Id == id ? update(t) : t)];

    private void AddTask()
    {
        var last = _tasks[^1];
        _added++;
        _tasks = [.. _tasks, new GanttTask(
            $"extra-{_added}", $"Follow-up {_added}", last.End.AddDays(1), last.End.AddDays(5), 0)];
    }

    private void RemoveLast() => _tasks = [.. _tasks.Take(_tasks.Count - 1)];
}
