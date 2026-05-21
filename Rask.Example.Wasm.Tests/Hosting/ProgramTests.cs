using Microsoft.Extensions.DependencyInjection;
using Rask.Example.Shared;
using Rask.Example.Shared.Demos;

namespace Rask.Example.Wasm.Tests.Hosting;

// Rask.Example.Wasm targets net10.0-browser and can't be directly invoked from a
// net10.0 test process. The DI registration shape lives in ExampleServiceCollectionExtensions
// inside Rask.Example.Shared so it can be unit-tested without booting a WASM runtime;
// Program.cs is then a one-liner that wires the same extension into WasmHostBuilder.
public sealed class ProgramTests
{
    [Fact]
    public void AddExampleServices_RegistersHttpClient_WithJsonPlaceholderBase()
    {
        var sp = new ServiceCollection()
            .AddExampleServices()
            .BuildServiceProvider();

        var http = sp.GetService<HttpClient>();
        Assert.NotNull(http);
        Assert.Equal(new Uri("https://jsonplaceholder.typicode.com/"), http!.BaseAddress);
    }

    [Fact]
    public void AddExampleServices_RegistersBannedWordService_AsSingleton()
    {
        var sp = new ServiceCollection()
            .AddExampleServices()
            .BuildServiceProvider();

        var a = sp.GetService<IBannedWordService>();
        var b = sp.GetService<IBannedWordService>();
        Assert.NotNull(a);
        Assert.Same(a, b);
        Assert.IsType<BannedWordService>(a);
    }

    [Fact]
    public void AddExampleServices_ReturnsSameServiceCollection_ForChaining()
    {
        var sc = new ServiceCollection();
        var returned = sc.AddExampleServices();
        Assert.Same(sc, returned);
    }
}
