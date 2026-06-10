namespace Rask.Wasm.Tests.Session;

[Collection("WasmSession")]
public class HandlerScopeTests
{
    [Fact]
    public async Task RequestRenderAsync_WhenInHandlerScopeTrue_ReturnsImmediatelyWithoutAcquiringLock()
    {
        var (session, _) = NewSession();
        session.InHandlerScope = true;

        var task = session.RequestRenderAsync();
        var completed = await Task.WhenAny(task, Task.Delay(500));

        Assert.Same(task, completed);
        Assert.True(task.IsCompletedSuccessfully);
    }
}
