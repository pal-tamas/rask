using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Wasm.Authentication;

/// <summary>
///     The browser host's <see cref="HttpClient" /> handler that hears Rask's own API refuse the tab: a <c>401</c> from
///     the page's own origin makes a tab that had a user ask who the browser carries now (<see cref="WasmUserWatch" />).
/// </summary>
/// <remarks>
///     <para>
///         A <c>401</c> is the server saying the request carried nobody it accepts. When the tab drew its page for
///         somebody, that is the browser host's refused attach: the sign-in may have ended, in another tab or by a
///         cookie that lapsed. The refusal is answered as the caller always read it — this only asks, and the answer
///         sends the page to the sign-in page when the browser carries nobody now. A wrong password on a sign-in
///         form is a <c>401</c> too, and changes nothing: the tab asking had nobody to lose, or still has them.
///     </para>
///     <para>
///         The answer is waited for before the caller reads the refusal, so the render its handler ends with already
///         finds the page on its way to the sign-in page. Another origin's <c>401</c> is not about this app's user.
///     </para>
/// </remarks>
internal sealed class SignInRefusals(IServiceProvider services, Uri origin) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || !IsOwnOrigin(request.RequestUri))
        {
            return response;
        }

        // Resolved at the refusal, not when the client is made: the watch reads the user provider, whose own client
        // may be this one.
        if (services.GetService<WasmUserWatch>() is { } watch)
        {
            await watch.Refused().ConfigureAwait(false);
        }

        return response;
    }

    // The client resolved a relative request against the page's origin before it reached here.
    private bool IsOwnOrigin(Uri? uri) =>
        uri is { IsAbsoluteUri: true } absolute
        && Uri.Compare(absolute, origin, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
}
