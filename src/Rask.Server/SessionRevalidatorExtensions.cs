using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Core.Authentication;
using Rask.Data;

namespace Rask.Server;

/// <summary>Registers a live session's revalidator written against the app's own user type.</summary>
public static class SessionRevalidatorExtensions
{
    /// <summary>
    ///     Asks <typeparamref name="T" /> before every event of a live page whether its user still stands — not
    ///     disabled, the same rights — in place of any other <see cref="ISessionRevalidator" />.
    /// </summary>
    /// <typeparam name="T">The app's <see cref="SessionRevalidator{TUser}" />, made a singleton.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The steps that say how long a loaded user is kept: <c>.Keep(5.Seconds).ForgetOnSave&lt;User&gt;()</c>.</returns>
    /// <remarks>
    ///     A later call replaces an earlier one, and Rask.Auth's own revalidator with it.
    /// </remarks>
    public static SessionRevalidatorBuilder AddSessionRevalidator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services)
        where T : SessionRevalidator
    {
        ArgumentNullException.ThrowIfNull(services);

        var keeping = new SessionUserKeeping();
        services.RemoveAll<ISessionRevalidator>();
        services.RemoveAll<KeptSessionUsers>();
        services.TryAddSingleton<T>();

        // The kept users know nothing of T: the save interceptor reaches them, and T may need the very context
        // factory that interceptor is being built for.
        services.AddSingleton(sp => new KeptSessionUsers(keeping, sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISavedTypeWatcher, KeptSessionUsers>(
            static sp => sp.GetRequiredService<KeptSessionUsers>()));
        services.AddSingleton<ISessionRevalidator>(static sp =>
            new KeptSessionRevalidator(sp.GetRequiredService<T>(), sp.GetRequiredService<KeptSessionUsers>()));

        return new SessionRevalidatorBuilder(services, keeping);
    }
}
