namespace Rask.Core.Live;

public interface IRenderHandle
{
    Task RequestRenderAsync();
    internal Task RenderInScopeAsync() => Task.CompletedTask;
}
