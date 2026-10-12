namespace Rask.Wasm.Tests.Session;

[Collection("WasmSession")]
public class HandlerScopeTests
{
    [Fact]
    public void A_render_request_inside_the_handler_scope_returns_at_once_without_taking_the_lock()
    {
        var (session, _) = NewSession();
        session.InHandlerScope = true;

        var task = session.RequestRender();

        // "At once" is before the call returns: the request is parked for the dispatch that holds the scope.
        Assert.True(task.IsCompletedSuccessfully);
    }
}
