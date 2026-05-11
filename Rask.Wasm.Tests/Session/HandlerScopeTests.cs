using Rask.Core.Routing;
using Rask.Wasm.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Wasm.Tests.Session;

[Collection("WasmSession")]
public class HandlerScopeTests
{
    [Fact]
    public async Task RequestRenderAsync_WhenInHandlerScopeTrue_ReturnsImmediatelyWithoutAcquiringLock()
    {
        var session = NewSession();
        session.InHandlerScope = true;

        var task = session.RequestRenderAsync();
        var completed = await Task.WhenAny(task, Task.Delay(500));

        Assert.Same(task, completed);
        Assert.True(task.IsCompletedSuccessfully);
    }

    private static WasmLiveSession NewSession()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RouteState>();
        services.AddSingleton<Navigator>();
        var provider = services.BuildServiceProvider();
        var app = ActivatorUtilities.CreateInstance<StubApp>(provider);
        return new WasmLiveSession(app, provider);
    }
}
