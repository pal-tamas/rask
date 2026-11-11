namespace Rask.Core.Caching;

/// <summary>
///     The headers of the request a render answers — the page's own <c>GET</c>, or the request that opened its live
///     connection — for <c>Cached.VaryByHeader(…)</c>.
/// </summary>
/// <remarks>The server registers one; the browser has no request to read, so a header-keyed fragment there is drawn live.</remarks>
internal interface IRequestHeaders
{
    /// <summary>
    ///     The value of <paramref name="name" />, several joined by commas; empty when the request did not send it;
    ///     null when there is no request to read at all.
    /// </summary>
    string? Header(string name);
}
