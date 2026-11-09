using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server;

/// <summary>
///     <see cref="ISessions" /> with nothing injected — from a handler, a render, a request, a job:
/// </summary>
/// <remarks>
///     <code>
///     choices.Chosen(userId, partner);
///     Sessions.Forget(http.User);   // every open tab of this user asks the revalidator again at its next event
///     </code>
///     <para>
///         Each call reaches the kept answers of the app it runs in. With no <c>AddSessionRevalidator&lt;T&gt;()</c>
///         nothing is kept, so there is nothing to forget and it does nothing. Outside any work — a hosted service, a
///         timer started at boot — it throws; inject <see cref="ISessions" /> there instead.
///     </para>
/// </remarks>
public static class Sessions
{
    /// <inheritdoc cref="ISessions.Forget(string)" />
    /// <exception cref="InvalidOperationException">Called outside any work in progress.</exception>
    public static void Forget(string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        Resolve()?.Forget(userId);
    }

    /// <inheritdoc cref="ISessions.Forget(ClaimsPrincipal)" />
    /// <exception cref="InvalidOperationException">Called outside any work in progress.</exception>
    public static void Forget(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        Resolve()?.Forget(user);
    }

    private static ISessions? Resolve()
    {
        var services = Ambient.Services ?? throw new InvalidOperationException(
            "Sessions.Forget(…) was called outside any work in progress — an event handler, a render, a request or a "
            + "job — so there is no app to reach. Inject ISessions in the constructor there instead.");
        return services.GetService<ISessions>();
    }
}
