using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>The WASM service worker's offline cache, reached from the page.</summary>
internal static class OfflineCache
{
    /// <summary>
    ///     Empties it. Called on sign-out: what the worker kept, it kept for whoever was signed in, and a
    ///     shared device must not replay it to the next person.
    /// </summary>
    /// <param name="js">The browser.</param>
    public static async ValueTask Clear(IJSRuntime js)
    {
        try
        {
            await js.InvokeVoidAsync("__raskOffline.clear").ConfigureAwait(false);
        }
        catch (JSException)
        {
            // A page that ships its own runtime has no helper to call. Signing out still has to finish.
        }
    }
}
