using Microsoft.Extensions.DependencyInjection;
using Rask.Site;
using Rask.Site.Features;

namespace Rask.Site.Tests.Hosting;

// Rask.Site targets net10.0-browser and can't be directly invoked from a
// net10.0 test process. The DI registration shape lives in ExampleServiceCollectionExtensions
// inside Rask.Site so it can be unit-tested without booting a WASM runtime;
// Program.cs is then a one-liner that wires the same extension into WasmHostBuilder.
public sealed class ProgramTests
{
    [Fact]
    public void AddExampleServices_RegistersHttpClient_WithSuppliedBaseAddress()
    {
        // The base address is now host-specific: Program.cs passes the page origin
        // (WasmHostBuilder.BaseAddress) so the HTTP demo fetches a local static file.
        // The resolver is invoked lazily on first HttpClient resolution.
        var origin = new Uri("https://example.test/app/");
        var sp = new ServiceCollection()
            .AddExampleServices(_ => origin)
            .BuildServiceProvider();

        var http = sp.GetService<HttpClient>();
        Assert.NotNull(http);
        Assert.Equal(origin, http!.BaseAddress);
    }

    [Fact]
    public void AddExampleServices_RegistersBannedWordService_AsSingleton()
    {
        var sp = new ServiceCollection()
            .AddExampleServices(_ => new Uri("http://localhost/"))
            .BuildServiceProvider();

        var a = sp.GetService<IBannedWordService>();
        var b = sp.GetService<IBannedWordService>();
        Assert.NotNull(a);
        Assert.Same(a, b);
        Assert.IsType<BannedWordService>(a);
    }

    [Fact]
    public void AddExampleServices_RegistersTheSystemClock_KeepingOneAlreadyRegistered()
    {
        // HttpFetchDemo injects the TimeProvider its retry delays and attempt deadline run on (#1067), so
        // the site must register one — and must not replace a clock registered before it, which is how a
        // host or a test supplies its own.
        var site = new ServiceCollection()
            .AddExampleServices(_ => new Uri("http://localhost/"))
            .BuildServiceProvider();
        Assert.Same(TimeProvider.System, site.GetService<TimeProvider>());

        var own = new OwnClock();
        var hosted = new ServiceCollection()
            .AddSingleton<TimeProvider>(own)
            .AddExampleServices(_ => new Uri("http://localhost/"))
            .BuildServiceProvider();
        Assert.Same(own, hosted.GetService<TimeProvider>());
    }

    private sealed class OwnClock : TimeProvider;

    [Fact]
    public void AddExampleServices_ReturnsSameServiceCollection_ForChaining()
    {
        var sc = new ServiceCollection();
        var returned = sc.AddExampleServices(_ => new Uri("http://localhost/"));
        Assert.Same(sc, returned);
    }
}
