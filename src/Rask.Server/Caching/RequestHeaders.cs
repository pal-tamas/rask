using Microsoft.AspNetCore.Http;
using Rask.Core.Caching;

namespace Rask.Server.Caching;

/// <summary>
///     The headers of the request in progress — a page's <c>GET</c>, or the one that opened its live connection, which
///     the same browser sent with the same headers — for <c>Cached.VaryByHeader(…)</c>.
/// </summary>
internal sealed class RequestHeaders(IHttpContextAccessor accessor) : IRequestHeaders
{
    public string? Header(string name)
    {
        if (accessor.HttpContext?.Request.Headers is not { } headers)
        {
            return null;
        }

        return headers.TryGetValue(name, out var values) ? values.ToString() : string.Empty;
    }
}
