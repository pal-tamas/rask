namespace Rask.Site.Features;

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
                Span.Class($"{Tw.BadgePrimary} text-base")[$"Render #{++_renderCount}"],
                // The handler just records the click; Rask re-renders the component that owns the
                // callback (this probe — the lambda closes over its state) right after it runs, so the
                // badge repaints with no StateHasChanged (RASK026). Works the same through UiButton,
                // which forwards the callback down to the native <button>.
                UiButton.Label("Trigger re-render").Icon(UiIconName.Retry).Tone(UiTone.Primary)
                    .OnClick(() => _log.Add("Trigger re-render (button click)"))
            ],
            H3.Class("text-base font-semibold text-ui-muted uppercase text-sm")["Hook log"],
            Ol.Class($"{Tw.ListGroup} list-decimal list-inside divide-y divide-ui-line")[
                _log.Select((l, i) => Li.Key(i).Class($"{Tw.ListGroupItem} ps-2 text-sm")[Code.Class("text-sm")[l]])
                    .ToArray()]
        ];
}
