using Microsoft.Extensions.DependencyInjection;
using Rask.Core;

namespace Rask.Wasm.Tests;

// The WASM end of the cross-host parity gate — see the sibling test in Rask.Server.Tests. Built from
// WasmHostBuilder itself rather than the session harness: the harness wires
// only what the session tests need, so asserting against it would prove nothing about what a real app gets.
public sealed class HostContractParityTests
{
    [Fact]
    public void WasmHostBuilder_ResolvesEveryCoreHostContract()
    {
        var builder = WasmHostBuilder.CreateDefault();
        using var provider = builder.Services.BuildServiceProvider();

        var missing = RaskHostContracts.All
            .Where(t => provider.GetService(t) is null)
            .Select(t => t.Name)
            .Order()
            .ToList();

        Assert.Empty(missing);
    }

    // The framework's registrations are all TryAdd, so an app that wants its own implementation of a Core
    // contract must be able to win by registering first. Guarding one representative keeps the parity test
    // above from being "satisfied" by a future host that hard-registers and locks apps out.
    [Fact]
    public void AppRegistration_BeforeTheFrameworkDefaults_Wins()
    {
        var builder = WasmHostBuilder.CreateDefault();
        var http = new HttpClient { BaseAddress = new Uri("https://example.test/") };
        builder.Services.AddSingleton(http);

        using var provider = builder.Services.BuildServiceProvider();

        Assert.Same(http, provider.GetService<HttpClient>());
    }
}
