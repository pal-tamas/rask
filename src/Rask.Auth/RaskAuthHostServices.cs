using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Core.Authentication;

namespace Rask.Auth;

/// <summary>
///     What this package adds to the accounts battery that <c>Rask.Auth.Api</c> cannot.
/// </summary>
/// <remarks>
///     <para>
///         The split is <c>Rask.Core</c>: the battery's Identity half, its <c>/api/auth</c> endpoints
///         and its cookie are host-neutral and live in <c>Rask.Auth.Api</c>, while the two things here
///         need a renderer — an <see cref="IAuth" /> that issues its session through the host's
///         <see cref="IAuthSignIn" /> relay, and email bodies rendered from real Rask components. A
///         host that renders nothing carries no <c>Rask.Core</c> at all, so a battery that reached for
///         either could not run there (#1069).
///     </para>
///     <para>
///         Contributed from a module initializer rather than from an <c>AddRaskAuth</c> of its own.
///         Two same-named extension methods in one namespace would be ambiguous the moment both
///         packages are referenced, and two registration paths are two orders that can drift — the
///         hazard <c>AddRask</c> already documents (RASK056). There is one <c>AddRaskAuth</c>, and it
///         asks here whether a host has anything to add.
///     </para>
/// </remarks>
internal sealed class RaskAuthHostServices : IAuthHostServices
{
    /// <summary>
    ///     Runs when this assembly is loaded, which is before any <c>AddRaskAuth</c> call in the app
    ///     that referenced it — a module initializer runs at first use of the assembly, and an app
    ///     reaches <c>AddRaskAuth</c> through it.
    /// </summary>
    // CA2255 warns that a module initializer in a library surprises its consumer. That is the whole
    // mechanism here and it is the documented "advanced" case: this package's entire job is to add
    // itself to a battery that must not reference it, and doing it from AddRaskAuth would need a
    // second same-named extension method (ambiguous) or an explicit call the app must remember
    // (a battery that is on by default cannot ask for one). The initializer assigns one property.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register() => AuthHost.Services = new RaskAuthHostServices();

    /// <inheritdoc />
    public void Register<TUser>(IServiceCollection services)
        where TUser : IdentityUser, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IAuth, ServerAuth<TUser>>();

        // AddRaskAuth calls this BEFORE it TryAdds its plain-HTML default, so this registration is
        // the one that takes — see the note there on TryAdd being first-wins.
        services.TryAddSingleton<IAuthEmailBodies, ComponentAuthEmailBodies>();
    }
}
