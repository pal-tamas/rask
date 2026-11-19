using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rask.Hosting.Shared;

namespace Rask.Core.Tests.Security;

// SameOrigin compared the host alone, so another app on the same machine — another port, or an http page beside an
// https one — passed as the page's own. Scheme, host and port are compared now; a proxy that hides them is named by
// Rask:PublicOrigin (or Rask:Auth:PublicOrigin) instead.
public sealed class SameOriginTests
{
    [Theory]
    [InlineData("https", "app.example", "https://app.example")]
    [InlineData("https", "app.example:8443", "https://app.example:8443")]
    [InlineData("http", "localhost:5000", "http://localhost:5000")]
    [InlineData("http", "app.example", "http://app.example:80")]
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
    [InlineData("http", "app.example", "https://app.example")] // TLS ended at a proxy nobody configured
    [InlineData("http", "app.example", "http://app.example:8080")]
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

    [Theory]
    [InlineData(SameOrigin.PublicOriginSetting, "https://app.example")]
    [InlineData(SameOrigin.PublicOriginSetting, "https://other.example, https://app.example")]
    [InlineData(SameOrigin.AuthPublicOriginSetting, "https://app.example")]
    public void A_configured_public_origin_lets_in_a_page_a_proxy_hides_the_scheme_of(string setting, string value)
    {
        var request = Request("http", "localhost:8080", "https://app.example", (setting, value));

        var allowed = SameOrigin.Allows(request);

        Assert.True(allowed);
    }

    [Fact]
    public void A_configured_public_origin_lets_in_no_other_origin()
    {
        var request = Request("http", "localhost:8080", "https://app.example:8443", (SameOrigin.PublicOriginSetting, "https://app.example"));

        var allowed = SameOrigin.Allows(request);

        Assert.False(allowed);
    }

    private static HttpRequest Request(string scheme, string host, string? origin, (string Key, string Value)? setting = null)
    {
        var context = new DefaultHttpContext();
        if (setting is { } named)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection([new KeyValuePair<string, string?>(named.Key, named.Value)])
                .Build();
            context.RequestServices = new ServiceCollection().AddSingleton<IConfiguration>(configuration).BuildServiceProvider();
        }

        var request = context.Request;
        request.Scheme = scheme;
        request.Host = HostString.FromUriComponent(host);
        if (origin is not null)
        {
            request.Headers.Origin = origin;
        }

        return request;
    }
}
