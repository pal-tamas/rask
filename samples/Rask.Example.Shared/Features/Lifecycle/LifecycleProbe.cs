namespace Rask.Example.Shared.Features;

public sealed class LifecycleProbe : Component
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

    protected override RenderResult Render() =>
        [
            Div(Class: "d-flex align-items-center gap-3 mb-3")[
                Span(Class: "badge text-bg-primary fs-6")[$"Render #{++_renderCount}"],
                // A bare event handler is enough: Rask re-renders the component that owns the callback
                // after it runs, so the click repaints this probe with no StateHasChanged (RASK026).
                Button(
                    Class: "btn btn-primary btn-sm",
                    OnClick: () => { })[I(Class: "bi bi-arrow-clockwise me-1"), "Trigger re-render"]
            ],
            H3(Class: "h6 text-secondary text-uppercase small")["Hook log"],
            Ol(Class: "list-group list-group-numbered list-group-flush")[
                _log.Select((l, i) => Li(Key: i, Class: "list-group-item ps-2 small")[Code(Class: "small")[l]])
                    .ToArray()]
        ];
}
