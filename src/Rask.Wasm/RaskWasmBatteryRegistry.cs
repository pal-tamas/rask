using Microsoft.Extensions.DependencyInjection;

namespace Rask.Wasm;

/// <summary>
/// Where the browser batteries hand themselves to <see cref="WasmHostBuilder"/>.
/// </summary>
/// <remarks>
/// The batteries have to be wired <em>before</em> the service provider is built, and only the host knows
/// when that is: it applies the wiring at the last moment services can still be added, after everything
/// <c>Program.cs</c> said. The default is this package's own (<c>WasmHostBuilderExtensions.Wire</c>); a test
/// substitutes its own with <see cref="Use"/>.
/// </remarks>
public static class RaskWasmBatteryRegistry
{
    private static Action<WasmHostBuilder, IServiceCollection>? _wire;

    /// <summary>Replaces the battery wiring — for a test that wants to see what the host applies.</summary>
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

    /// <summary>Applies the wiring: the substitute if a test set one, else this package's own.</summary>
    internal static void Apply(WasmHostBuilder host, IServiceCollection services) =>
        (_wire ?? WasmHostBuilderExtensions.Wire)(host, services);

    internal static void Reset() => _wire = null;
}
