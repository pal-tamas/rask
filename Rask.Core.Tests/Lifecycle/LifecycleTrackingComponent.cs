using Rask.Core.Components;

namespace Rask.Core.Tests.Lifecycle;

internal sealed class LifecycleTrackingComponent : Component
{
    public int MountCount;
    public int MountAsyncCount;
    public Func<Task>? OnMountAsyncImpl;
    public int PropsChangedCount;
    public int PropsChangedAsyncCount;
    public int RenderedCount;
    public int RenderCount;
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

    protected override Component Render()
    {
        RenderCount++;
        return new Span(null, new Text($"r{RenderCount}"));
    }
}
