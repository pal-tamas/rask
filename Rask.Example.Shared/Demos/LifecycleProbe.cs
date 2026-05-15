namespace Rask.Example.Shared.Demos;

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

    protected override Component Render()
    {
        _renderCount++;
        return Fragment()[
            Div(Class: "d-flex align-items-center gap-3 mb-3")[
                Span(Class: "badge text-bg-primary fs-6")[$"Render #{_renderCount}"],
                Button(
                    Class: "btn btn-primary btn-sm",
                    OnClick: () => StateHasChanged())[I(Class: "bi bi-arrow-clockwise me-1"), "Trigger re-render"]
            ],
            H3(Class: "h6 text-secondary text-uppercase small")["Hook log"],
            Ol(Class: "list-group list-group-numbered list-group-flush")[_log.Select(l => (Child)Li(Class: "list-group-item ps-2 small")[Code(Class: "small")[l]]).ToArray()]
        ];
    }
}

// Variant that surfaces every hook — including OnUnmount / OnUnmountAsync — to a parent-held
// log so the unmount entries survive the probe being torn down. The parent owns the list.
public sealed class LifecycleCycleProbe : Component
{
    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    protected override void OnMount() => Log($"#{InstanceId} OnMount");

    protected override async Task OnMountAsync()
    {
        Log($"#{InstanceId} OnMountAsync (start)");
        await Task.Delay(150);
        Log($"#{InstanceId} OnMountAsync (after 150ms await)");
    }

    protected override void OnUnmount() => Log($"#{InstanceId} OnUnmount");

    protected override Task OnUnmountAsync()
    {
        Log($"#{InstanceId} OnUnmountAsync");
        return Task.CompletedTask;
    }

    protected override Component Render() =>
        Div(Class: "d-flex align-items-center gap-2")[
            Span(Class: "badge text-bg-success")[$"#{InstanceId} alive"],
            Span(Class: "text-secondary small")["Unmount me to fire OnUnmount / OnUnmountAsync."]
        ];
}
