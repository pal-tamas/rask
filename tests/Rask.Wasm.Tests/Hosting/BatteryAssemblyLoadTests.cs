using Rask.Wasm;

namespace Rask.Wasm.Tests.Hosting;

// The host loads the Rask package itself, because nothing in an app does: Program.cs names only WasmHostBuilder, so the
// package's [ModuleInitializer] never ran and the batteries stayed off in every published app. The browser proof is
// the site journey's Rask.Query step, which nothing but these batteries registers.
public sealed class BatteryAssemblyLoadTests
{
    [Fact]
    public void An_app_without_the_Rask_package_is_left_alone() =>
        Assert.False(RaskWasmBatteryRegistry.LoadBatteries("Rask.NoSuchBatteries"));

    [Fact]
    public void The_package_is_loaded_by_its_assembly_name() =>
        Assert.Equal("Rask", RaskWasmBatteryRegistry.BatteriesAssembly);

    [Fact]
    public void Loading_an_assembly_that_is_there_runs_its_initializer_and_says_so() =>
        Assert.True(RaskWasmBatteryRegistry.LoadBatteries(typeof(BatteryAssemblyLoadTests).Assembly.GetName().Name!));
}
