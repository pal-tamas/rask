using System.Reflection;
using System.Runtime.CompilerServices;
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
/// <c>Rask</c> package registers a delegate from a <c>[ModuleInitializer]</c>, and the host applies it at the
/// last moment services can still be added.
/// </para>
/// <para>
/// <b>The host loads the package itself.</b> A module initializer runs when its assembly is loaded, and nothing
/// in an app loads <c>Rask.dll</c>: <c>Program.cs</c> names only <c>WasmHostBuilder</c>, which lives here. Keeping
/// the assembly in the bundle (the package's <c>TrimmerRootAssembly</c>) was never enough on its own — the batteries
/// silently stayed off until something happened to touch a type in it. So <see cref="Apply"/> loads it by name
/// and runs its initializer first.
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

    /// <summary>The assembly whose initializer registers the wiring: the <c>Rask</c> package.</summary>
    internal const string BatteriesAssembly = "Rask";

    /// <summary>Applies the wiring, if any. A no-op on an app that references only <c>Rask.Wasm</c>.</summary>
    internal static void Apply(WasmHostBuilder host, IServiceCollection services)
    {
        if (_wire is null)
        {
            LoadBatteries(BatteriesAssembly);
        }

        _wire?.Invoke(host, services);
    }

    /// <summary>
    /// Loads <paramref name="assemblyName" /> and runs its module initializer; <see langword="false" /> when the app
    /// does not carry it.
    /// </summary>
    internal static bool LoadBatteries(string assemblyName)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.Load(assemblyName);
        }
        catch (FileNotFoundException)
        {
            // An app on Rask.Wasm alone: no batteries, by choice.
            return false;
        }

        // Runs at most once per module, so a second host in the same process is not wired twice.
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        return true;
    }

    /// <summary>Forgets the registered wiring. For tests only: this is process-wide state.</summary>
    internal static void Reset() => _wire = null;
}
