using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Core.Authentication;
using Rask.Data;

namespace Rask.Server;

/// <summary>Registers the app's own session revalidator, with its answers kept.</summary>
public static class SessionRevalidatorExtensions
{
    /// <summary>
    ///     Asks <typeparamref name="T" /> before every event of a live page whether its user still stands — not
    ///     disabled, the same rights — in place of any other <see cref="ISessionRevalidator" />.
    /// </summary>
    /// <typeparam name="T">The app's <see cref="ISessionRevalidator" />, made a singleton.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The steps that say how long an answer is kept: <c>.Keep(5.Seconds).ForgetOnSave&lt;User&gt;()</c>.</returns>
    /// <remarks>
    ///     A later call replaces an earlier one, and Rask.Auth's own revalidator with it.
    /// </remarks>
    public static SessionRevalidatorBuilder AddSessionRevalidator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services)
        where T : class, ISessionRevalidator
    {
        ArgumentNullException.ThrowIfNull(services);

        var keeping = new SessionUserKeeping();
        services.RemoveAll<ISessionRevalidator>();
        services.RemoveAll<KeptSessionAnswers>();
        services.TryAddSingleton<T>();

        // The kept answers know nothing of T: the save interceptor reaches them, and T may need the very context
        // factory that interceptor is being built for.
        services.AddSingleton(sp => new KeptSessionAnswers(keeping, sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISavedTypeWatcher, KeptSessionAnswers>(
            static sp => sp.GetRequiredService<KeptSessionAnswers>()));
        services.RemoveAll<ISessions>();
        services.AddSingleton<ISessions>(static sp => sp.GetRequiredService<KeptSessionAnswers>());
        services.AddSingleton<ISessionRevalidator>(static sp =>
            new KeptSessionRevalidator(sp.GetRequiredService<T>(), sp.GetRequiredService<KeptSessionAnswers>()));

        return new SessionRevalidatorBuilder(services, keeping);
    }
}
