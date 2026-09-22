namespace Rask.Core.Tests.Lifecycle;

// Each hook used to come as a synchronous and an asynchronous twin, and the tests count both. The twins are one
// Task-returning hook now, so each counter pair moves together — kept as a pair so the tests that assert "ran
// once" against either name still say what they said.
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
    public int FirstRenderCount;
    private bool _firstRenderPending;

    // One entry per Rendered call: true for the render FirstRender ran on.
    public List<bool> RenderedFlags { get; } = new();

    protected override Task Mount()
    {
        MountCount++;
        MountAsyncCount++;
        return OnMountAsyncImpl?.Invoke() ?? Task.CompletedTask;
    }

    protected override Task Updated()
    {
        PropsChangedCount++;
        PropsChangedAsyncCount++;
        return Task.CompletedTask;
    }

    protected override Task FirstRender()
    {
        FirstRenderCount++;
        _firstRenderPending = true;
        return Task.CompletedTask;
    }

    protected override Task Rendered()
    {
        RenderedCount++;
        RenderedFlags.Add(_firstRenderPending);
        _firstRenderPending = false;
        return Task.CompletedTask;
    }

    protected override Task Unmount()
    {
        UnmountCount++;
        OnUnmountImpl?.Invoke();
        UnmountAsyncCount++;
        return OnUnmountAsyncImpl?.Invoke() ?? Task.CompletedTask;
    }

    protected override Component? Render()
    {
        RenderCount++;
        return Span[Text.Value($"r{RenderCount}")];
    }
}
