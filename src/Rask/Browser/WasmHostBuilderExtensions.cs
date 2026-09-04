using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;
using Rask.Query;
using Rask.Wasm;

namespace Rask;

/// <summary>
/// The browser half of the <c>Rask</c> package: every battery it brings is on, and this is where an app
/// says which it does without.
/// </summary>
/// <remarks>
/// An extension on the existing <see cref="WasmHostBuilder"/> rather than a second entry point. A WASM
/// <c>Program.cs</c> was never the three-hundred-line problem the server's was — <c>CreateDefault</c> plus
/// <c>RunAsync</c> is already short — so what was missing here is the batteries, not a new way to start.
/// </remarks>
public static class WasmHostBuilderExtensions
{
    // Keyed on the builder, not a static field: a browser process hosts exactly one app, so a static
    // would work in production and quietly fail in a test that builds two.
    private static readonly ConditionalWeakTable<WasmHostBuilder, RaskWasmOptions> Options = new();

    // Runs before Main, so the host finds the wiring already waiting.
    //
    // CA2255 warns off module initializers in libraries, and is usually right: a library that runs code
    // on load surprises whoever loaded it. This is the case the rule names as the exception — handing a
    // hook to a host that cannot reference back. It registers a delegate and touches nothing else, and
    // the alternative is asking every app to write a line whose only purpose is to say "yes, really".
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register() => RaskWasmBatteryRegistry.Use(Wire);

    /// <summary>Says which batteries this app does without.</summary>
    /// <example>
    /// <code>
    /// var host = WasmHostBuilder.CreateDefault();
    ///
    /// host.Configure(c => c.Query.Off());
    ///
    /// await host.RunAsync&lt;App&gt;();
    /// </code>
    /// </example>
    public static WasmHostBuilder Configure(this WasmHostBuilder host, Action<RaskWasmOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(configure);

        configure(Options.GetOrCreateValue(host));
        return host;
    }

    // Applied by the host just before it builds the provider, so everything Program.cs said has been said.
    // An app that never called Configure has no entry here and gets the defaults — every battery on.
    private static void Wire(WasmHostBuilder host, IServiceCollection services)
    {
        var options = Options.TryGetValue(host, out var configured) ? configured : new RaskWasmOptions();

        if (!options.Cqrs.Enabled)
        {
            return;
        }

        services.AddRaskCqrs();

        // Validation runs in the BROWSER too, and the server still runs it again.
        //
        // Two reasons it belongs here rather than only on the server. An invalid command should not cost
        // a round trip before the user is told which field is wrong; and a form and the command it
        // submits are validated by the same AbstractValidator<T>, so leaving this out would mean rules
        // that fire while typing and then appear to stop applying at submit.
        //
        // The server is still the authority. Nothing here can be trusted, and nothing here is trusted:
        // the same behavior runs again in RaskBatteryWiring before any handler is reached.
        services.AddRaskRequestValidation();

        // Rides with the dispatcher rather than being a decision of its own: a dispatcher without a cache
        // means every render refetches, which is the first thing anyone building over IDispatcher needs
        // solved. Turning the mediator off takes it too — there is nothing left to cache.
        if (options.Query.Enabled)
        {
            services.AddRaskQuery();
        }
    }
}
