using System.Net.Http.Headers;

namespace Rask.Example.Auth.WasmJwt;

// Attaches Authorization: Bearer <jwt> from the TokenStore to every outgoing request.
public sealed class BearerTokenHandler(TokenStore tokens) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (tokens.Token is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, ct);
    }
}
