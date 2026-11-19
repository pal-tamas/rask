using Microsoft.JSInterop;

namespace Rask.Server.SignOut;

/// <summary>The endpoint of the push subscription this browser holds, or null: no worker, no subscription, or no answer.</summary>
internal static class BrowserPushEndpoint
{
    // Never Navigator.ServiceWorker.Ready: on a page with no worker it never settles, and sign-out would wait on it.
    public static async Task<string?> Read()
    {
        try
        {
            foreach (var registration in await Rask.Web.Navigator.ServiceWorker.GetRegistrations().ConfigureAwait(false))
            {
                await using (registration.ConfigureAwait(false))
                {
                    var subscription = await registration.PushManager.GetSubscription().ConfigureAwait(false);
                    if (subscription is null)
                    {
                        continue;
                    }

                    await using (subscription.ConfigureAwait(false))
                    {
                        return await subscription.Endpoint.ConfigureAwait(false);
                    }
                }
            }
        }
        catch (JSException)
        {
            // A browser without workers or push, or one that refused: there is nothing to hand back.
        }

        return null;
    }
}
