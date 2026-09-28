using Microsoft.Extensions.DependencyInjection;

namespace Rask.Auth;

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
        where TUser : Authenticatable, new();
}
