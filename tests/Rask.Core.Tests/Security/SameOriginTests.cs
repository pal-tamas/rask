using Microsoft.AspNetCore.Http;
using Rask.Hosting.Shared;

namespace Rask.Core.Tests.Security;

// SameOrigin compared the host alone, so another app on the same machine — another port, or an http page beside an
// https one — passed as the page's own. It is lenient only where a proxy hides the port or the scheme.
public sealed class SameOriginTests
{
    [Theory]
    [InlineData("https", "app.example", "https://app.example")]
    [InlineData("https", "app.example:8443", "https://app.example:8443")]
    [InlineData("http", "localhost:5000", "http://localhost:5000")]
    [InlineData("http", "app.example", "https://app.example")] // TLS ended at a proxy that forwards no port
    [InlineData("http", "app.example", "http://app.example:8080")] // no port on Host: the proxy hides it
    public void A_page_of_the_host_itself_is_allowed(string scheme, string host, string origin)
    {
        var request = Request(scheme, host, origin);

        var allowed = SameOrigin.Allows(request);

        Assert.True(allowed);
    }

    [Theory]
    [InlineData("http", "localhost:5000", "http://localhost:5001")]
    [InlineData("https", "app.example:8443", "https://app.example:9443")]
    [InlineData("https", "app.example", "http://app.example")]
    [InlineData("https", "app.example", "https://evil.example")]
    public void Another_port_scheme_or_host_is_refused(string scheme, string host, string origin)
    {
        var request = Request(scheme, host, origin);

        var allowed = SameOrigin.Allows(request);

        Assert.False(allowed);
    }

    [Fact]
    public void A_request_with_neither_origin_nor_referer_is_allowed_as_before()
    {
        var request = Request("https", "app.example", origin: null);

        var allowed = SameOrigin.Allows(request);

        Assert.True(allowed);
    }

    private static HttpRequest Request(string scheme, string host, string? origin)
    {
        var request = new DefaultHttpContext().Request;
        request.Scheme = scheme;
        request.Host = HostString.FromUriComponent(host);
        if (origin is not null)
        {
            request.Headers.Origin = origin;
        }

        return request;
    }
}
