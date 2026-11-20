using Microsoft.Extensions.DependencyInjection;
using Rask.Auth;
using Rask.WebPush;

namespace Rask.Server.SignOut;

/// <summary>
///     Hands this browser's push subscription back as its user signs out on the Server host (<see cref="IPush.Release" />),
///     so the next user to sign in on it claims it rather than finding it the last one's. Nothing when Web Push is off or
///     the browser never subscribed.
/// </summary>
internal sealed class PushSignOutRelease : ISignOutStep
{
    private readonly IServiceProvider _services;
    private readonly Func<Task<string?>> _endpoint;

    public PushSignOutRelease(IServiceProvider services)
        : this(services, BrowserPushEndpoint.Read)
    {
    }

    internal PushSignOutRelease(IServiceProvider services, Func<Task<string?>> endpoint)
    {
        _services = services;
        _endpoint = endpoint;
    }

    public async Task BeforeSignOut()
    {
        if (_services.GetService<IPush>() is not { } push || await _endpoint().ConfigureAwait(false) is not { } endpoint)
        {
            return;
        }

        await push.Release(endpoint).ConfigureAwait(false);
    }
}
