namespace Rask.Wasm.Tests.Session;

[Collection("WasmSession")]
public class HandlerScopeTests
{
    [Fact]
    public async Task A_render_request_inside_the_handler_scope_returns_at_once_without_taking_the_lock()
    {
        var (session, _) = NewSession();
        session.InHandlerScope = true;

        var task = session.RequestRenderAsync();
        var completed = await Task.WhenAny(task, Task.Delay(500));

        Assert.Same(task, completed);
        Assert.True(task.IsCompletedSuccessfully);
    }
}
