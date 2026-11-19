using System.Net.Http.Json;
using Microsoft.JSInterop;

namespace Rask.Auth.Client;

/// <summary>
///     Hands this browser's push subscription back as its user signs out, while the cookie is still theirs: the server lets
///     go of the row (<c>POST /_rask/push/release</c>), and the next user to sign in on the browser claims it by
///     subscribing. Nothing when the browser holds no subscription, the page ships no helper, or the app has no Web Push
///     (the endpoint answers 404) — sign-out goes on either way.
/// </summary>
internal static class BrowserPush
{
    internal const string ReleasePath = "_rask/push/release";

    public static async Task Release(IJSRuntime js, HttpClient http)
    {
        string? endpoint;
        try
        {
            endpoint = await js.InvokeAsync<string?>("__raskPush.endpoint").ConfigureAwait(false);
        }
        catch (JSException)
        {
            return; // a page with its own runtime has no helper to ask
        }

        if (string.IsNullOrEmpty(endpoint))
        {
            return;
        }

        try
        {
            using var answer = await http.PostAsJsonAsync(ReleasePath, new PushReleaseRequest(endpoint), AuthJsonContext.Default.PushReleaseRequest)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // Offline: the row stays this user's until the next one subscribes elsewhere or it expires. Sign-out goes on.
        }
    }
}
