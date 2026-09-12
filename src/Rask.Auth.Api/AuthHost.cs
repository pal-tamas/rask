using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Auth;

/// <summary>
///     How a Rask host adds its own services to <c>AddRaskAuth</c>, without this package knowing it
///     exists.
/// </summary>
/// <remarks>
///     <para>
///         This package is the accounts battery for an ASP.NET host that renders no Rask components —
///         a TypeScript SPA host, a meta-framework host, or a plain ASP.NET app. It carries Identity,
///         the <c>/api/auth</c> endpoints and the cookie, and it deliberately does not reference
///         <c>Rask.Core</c>: Core is <c>IsPackable=false</c> and travels inside the host packages that
///         render components, so a battery that needs it simply cannot run anywhere else (#1069).
///     </para>
///     <para>
///         <c>Rask.Auth</c> — the same battery for an app that IS a Rask app — adds two things on top:
///         an <c>IAuth</c> bound to the host's sign-in relay, and email bodies rendered from real Rask
///         components. It contributes them through here, from a module initializer, so
///         <c>AddRaskAuth</c> stays one method with one registration order rather than two that can
///         drift.
///     </para>
///     <para>
///         Generic rather than reflective on purpose: <see cref="IAuthHostServices.Register{TUser}" />
///         is handed the closed user type, so nothing here needs <c>MakeGenericType</c> and the
///         trimmer can see every construction it has to keep.
///     </para>
/// </remarks>
public static class AuthHost
{
    /// <summary>
    ///     The host's contribution, or <c>null</c> on a host that renders no components.
    /// </summary>
    /// <remarks>
    ///     Set once, from a module initializer, by whichever host package is present. It is not a
    ///     collection: exactly one host package can be referenced at a time, because each one carries
    ///     its own copy of the renderer.
    /// </remarks>
    public static IAuthHostServices? Services { get; set; }

    /// <summary>The host package's name. It contributes itself when the CLR loads it.</summary>
    private const string HostAssembly = "Rask.Auth";

    /// <summary>
    ///     Makes sure the host package has had its chance to contribute, loading it if the CLR has not.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>Rask.Auth</c> contributes from a <c>[ModuleInitializer]</c>, and a module initializer
    ///         runs when the CLR <b>loads the assembly</b> — which it does lazily, at first use. Nothing
    ///         in an app necessarily uses it: the battery's entry point is <c>AddRaskAuth</c>, and that
    ///         lives here. So on a Rask host the app would call <c>AddRaskAuth</c>, this package would
    ///         see no contribution, and the app would start perfectly well with no <c>IAuth</c> and no
    ///         built-in sign-in pages — a silent downgrade, green build, no error until something
    ///         injected <c>IAuth</c>.
    ///     </para>
    ///     <para>
    ///         So the load is asked for by name rather than waited on. It is a no-op when the assembly
    ///         is already loaded, and <see cref="FileNotFoundException" /> is the ordinary answer on a
    ///         host that renders nothing — that app referenced this package alone, which is the
    ///         supported arrangement, not a misconfiguration.
    ///     </para>
    /// </remarks>
    internal static void EnsureHostLoaded()
    {
        if (Services is not null)
        {
            return;
        }

        try
        {
            var host = AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(HostAssembly));

            // Loading is not enough on its own. A [ModuleInitializer] is guaranteed to run before any
            // TYPE in the module is used, and the runtime honours that by deferring it until something
            // touches one — an Assembly.Load that nothing follows up on can leave it unrun. Asking for
            // the module constructor directly is the documented way to say "now", and it is idempotent,
            // so the ordinary case where the CLR already ran it costs nothing.
            RuntimeHelpers.RunModuleConstructor(host.ManifestModule.ModuleHandle);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            // Not referenced. This app has no renderer, which is what this package is for.
        }
    }
}

/// <summary>
///     What a Rask host contributes to the accounts battery.
/// </summary>
public interface IAuthHostServices
{
    /// <summary>
    ///     Adds the host's own auth services for <typeparamref name="TUser" />.
    /// </summary>
    /// <typeparam name="TUser">The application's user entity.</typeparam>
    /// <param name="services">The service collection <c>AddRaskAuth</c> is populating.</param>
    void Register<TUser>(IServiceCollection services)
        where TUser : IdentityUser, new();
}
