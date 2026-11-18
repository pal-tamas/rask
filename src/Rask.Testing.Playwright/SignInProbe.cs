using System.Globalization;
using Microsoft.Playwright;
using Rask.Wire;

namespace Rask.Testing;

/// <summary>
///     What an app answers a sign-in about who it is, read without a page: the question a cached sign-in is asked before
///     it is handed out again, so one a test signed out (its session gone on the server) is not.
/// </summary>
/// <remarks>
///     <para>
///         Two questions, the first that answers it: Rask.Auth's <c>/api/auth/me</c> (<c>200</c> signed in, <c>204</c>
///         nobody) where the app maps it — a WebAssembly app's host — and otherwise the sign-in page, unfollowed, which
///         sends a signed-in reader on (<c>[Guest]</c> on the page <c>rask new</c> writes) and shows a visitor the form.
///     </para>
///     <para>
///         The answer is compared with the one the same sign-in had when it was new rather than read as yes or no: an app
///         that answers neither question differently (its own sign-in, a sign-in page for everyone) answers the same
///         both times, and its sign-in is kept as it was.
///     </para>
/// </remarks>
internal static class SignInProbe
{
    private static readonly string Me = AuthApi.DefaultPrefix + AuthApi.Me;

    /// <summary>What the app answers <paramref name="storageState" /> about who it is.</summary>
    /// <param name="requests">Playwright's request API, which needs no browser.</param>
    /// <param name="address">The app.</param>
    /// <param name="storageState">The sign-in's cookies and storage, as Playwright's <c>StorageStateAsync()</c> writes them.</param>
    /// <param name="signInPage">The page the app sends a visitor to sign in.</param>
    /// <returns>The question and its status, <c>"/api/auth/me 200"</c>: equal for the same sign-in while it holds.</returns>
    public static async Task<string> Answer(IAPIRequest requests, Uri address, string storageState, string signInPage)
    {
        var context = await requests.NewContextAsync(new APIRequestNewContextOptions
        {
            BaseURL = address.ToString(),
            StorageState = storageState,
        }).ConfigureAwait(false);
        try
        {
            var me = await Status(context, Me).ConfigureAwait(false);
            return me is 200 or 204
                ? Said(Me, me)
                : Said(signInPage, await Status(context, signInPage).ConfigureAwait(false));
        }
        finally
        {
            await context.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<int> Status(IAPIRequestContext context, string path)
    {
        var answer = await context.GetAsync(path, new APIRequestContextOptions { MaxRedirects = 0 }).ConfigureAwait(false);
        try
        {
            return answer.Status;
        }
        finally
        {
            await answer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string Said(string path, int status) => path + " " + status.ToString(CultureInfo.InvariantCulture);
}
