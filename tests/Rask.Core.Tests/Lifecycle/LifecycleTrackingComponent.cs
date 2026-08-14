namespace Rask.Core.Tests.Lifecycle;

internal sealed partial class LifecycleTrackingComponent : Component
{
    public int MountAsyncCount;
    public int MountCount;
    public Func<Task>? OnMountAsyncImpl;
    public Func<Task>? OnUnmountAsyncImpl;
    public Action? OnUnmountImpl;
    public int PropsChangedAsyncCount;
    public int PropsChangedCount;
    public int RenderCount;
    public int RenderedCount;
    public int UnmountAsyncCount;
    public int UnmountCount;
    public List<bool> RenderedFlags { get; } = new();

    protected override void OnMount() => MountCount++;

    protected override Task OnMountAsync()
    {
        MountAsyncCount++;
        return OnMountAsyncImpl?.Invoke() ?? Task.CompletedTask;
    }

    protected override void OnPropsChanged() => PropsChangedCount++;

    protected override Task OnPropsChangedAsync()
    {
        PropsChangedAsyncCount++;
        return Task.CompletedTask;
    }

    protected override void OnRendered(bool firstRender)
    {
        RenderedCount++;
        RenderedFlags.Add(firstRender);
    }

    protected override void OnUnmount()
    {
        UnmountCount++;
        OnUnmountImpl?.Invoke();
    }

    protected override Task OnUnmountAsync()
    {
        UnmountAsyncCount++;
        return OnUnmountAsyncImpl?.Invoke() ?? Task.CompletedTask;
    }

    protected override Component? Render()
    {
        RenderCount++;
        return Span[Text.Value($"r{RenderCount}")];
    }
}
