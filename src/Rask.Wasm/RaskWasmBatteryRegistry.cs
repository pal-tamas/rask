using Microsoft.Extensions.DependencyInjection;

namespace Rask.Wasm;

/// <summary>
/// Where the <c>Rask</c> package's browser batteries hand themselves to <see cref="WasmHostBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Rask.Wasm</c> is the lean host and does not reference the batteries — an app that wants only the
/// component runtime should not download a mediator it never dispatches through. But the batteries have
/// to be wired <em>before</em> the service provider is built, and only the host knows when that is. So the
/// <c>Rask</c> package registers a delegate from a <c>[ModuleInitializer]</c>, which the runtime runs
/// before <c>Main</c>, and the host applies it at the last moment services can still be added.
/// </para>
/// <para>
/// The same shape as the server half, for the same reason: a package cannot call into its own consumer.
/// </para>
/// </remarks>
public static class RaskWasmBatteryRegistry
{
    private static Action<WasmHostBuilder, IServiceCollection>? _wire;

    /// <summary>
    /// Registers the battery wiring. Called from a <c>[ModuleInitializer]</c> in the <c>Rask</c> package;
    /// there is no reason to call it by hand.
    /// </summary>
    /// <param name="wire">
    /// Given the host being started and its services. The host is passed because that is what the
    /// caller's own <c>Configure</c> block was attached to — a process-wide options field would work in
    /// production, where a browser hosts exactly one app, and quietly fail in a test that builds two.
    /// </param>
    public static void Use(Action<WasmHostBuilder, IServiceCollection> wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        _wire = wire;
    }

    /// <summary>Applies the wiring, if any. A no-op on an app that references only <c>Rask.Wasm</c>.</summary>
    internal static void Apply(WasmHostBuilder host, IServiceCollection services) => _wire?.Invoke(host, services);

    /// <summary>Forgets the registered wiring. For tests only: this is process-wide state.</summary>
    internal static void Reset() => _wire = null;
}
