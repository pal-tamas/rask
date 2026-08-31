namespace Rask.Example.Shared.Features;

public sealed partial class LifecycleProbe : Component
{
    private readonly List<string> _log = new();
    private int _renderCount;

    protected override void OnMount() => _log.Add("OnMount");

    protected override async Task OnMountAsync()
    {
        _log.Add("OnMountAsync (start)");
        await Task.Delay(450);
        _log.Add("OnMountAsync (after 450ms await)");
    }

    protected override void OnPropsChanged() => _log.Add($"OnPropsChanged (render #{_renderCount + 1})");

    protected override Task OnPropsChangedAsync()
    {
        _log.Add("OnPropsChangedAsync");
        return Task.CompletedTask;
    }

    protected override void OnRendered(bool firstRender) =>
        _log.Add($"OnRendered(firstRender: {firstRender})");

    protected override Component? Render() =>
        [
            Div.Class("flex gap-3 items-center flex-wrap mb-3")[
                Span.Class($"{Ui.BadgePrimary} text-base")[$"Render #{++_renderCount}"],
                // The handler just records the click; Rask re-renders the component that owns the
                // callback (this probe — the lambda closes over its state) right after it runs, so the
                // badge repaints with no StateHasChanged (RASK026). Works the same through Button.Type("button").Class(Ui.BtnSecondary),
                // which forwards the callback down to the native <button>.
                Button.Type("button").Class(Ui.BtnPrimary)
                    .OnClick(() => _log.Add("Trigger re-render (button click)"))[Icon.Name(IconName.ArrowClockwise).Class("me-1"), "Trigger re-render"]
            ],
            H3.Class("text-base font-semibold text-slate-500 dark:text-slate-400 uppercase text-sm")["Hook log"],
            Ol.Class($"{Ui.ListGroup} list-decimal list-inside divide-y divide-slate-200 dark:divide-slate-700")[
                _log.Select((l, i) => Li.Key(i).Class($"{Ui.ListGroupItem} ps-2 text-sm")[Code.Class("text-sm")[l]])
                    .ToArray()]
        ];
}
