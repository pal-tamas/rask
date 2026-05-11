using Rask.Core.Components;

namespace Rask.Core.Tests.Lifecycle;

internal sealed class LifecycleTrackingComponent : Component
{
    public int AfterRenderCount;
    public int InitializedAsyncCount;
    public int InitializedCount;
    public Func<Task>? OnInitializedAsyncImpl;
    public int ParametersSetAsyncCount;
    public int ParametersSetCount;
    public int RenderCount;
    public Func<bool>? ShouldRenderFunc;
    public List<bool> AfterRenderFlags { get; } = new();

    protected override void OnInitialized() => InitializedCount++;

    protected override Task OnInitializedAsync()
    {
        InitializedAsyncCount++;
        return OnInitializedAsyncImpl?.Invoke() ?? Task.CompletedTask;
    }

    protected override void OnParametersSet() => ParametersSetCount++;

    protected override Task OnParametersSetAsync()
    {
        ParametersSetAsyncCount++;
        return Task.CompletedTask;
    }

    protected override void OnAfterRender(bool firstRender)
    {
        AfterRenderCount++;
        AfterRenderFlags.Add(firstRender);
    }

    protected override bool ShouldRender() => ShouldRenderFunc?.Invoke() ?? true;

    public override Component Render()
    {
        RenderCount++;
        return new Span(null, new Text($"r{RenderCount}"));
    }
}
