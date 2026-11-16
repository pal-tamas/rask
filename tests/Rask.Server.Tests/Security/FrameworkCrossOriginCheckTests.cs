using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Security;

/// <summary>
///     ASP.NET Core 11 checks every request a <c>WebApplication</c> serves for where it came from (<c>Sec-Fetch-Site</c>,
///     then <c>Origin</c>), but only on an endpoint whose <see cref="IAntiforgeryMetadata" /> asks for validation. Rask's
///     endpoints carry their own check — the host-only Origin match and the browser's <c>Sec-Fetch-Site</c>, refused
///     with a 403 the client reads — so none of them may ask for the framework's as well: two checks that disagree on
///     a <c>same-site</c> fetch would leave the live transport working on one and refused on the other.
/// </summary>
public class FrameworkCrossOriginCheckTests
{
    [Fact]
    public void No_endpoint_Rask_maps_asks_the_framework_s_cross_origin_check_to_validate_it()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var validated = endpoints
            .Where(static e => e.Metadata.GetMetadata<IAntiforgeryMetadata>() is { RequiresValidation: true })
            .Select(static e => e.RoutePattern.RawText)
            .ToList();

        Assert.Contains(endpoints, static e => e.RoutePattern.RawText == "/_rask/send");
        Assert.Contains(endpoints, static e => e.RoutePattern.RawText == "/_rask/upload");
        Assert.Contains(endpoints, static e => e.RoutePattern.RawText == "/_rask/auth/redeem");
        Assert.Empty(validated);
    }

    [Fact]
    public async Task An_upload_the_browser_says_came_from_the_page_itself_is_taken()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var sessionId = await SessionAsync(host);
        using var request = Upload(sessionId);
        request.Headers.Add("Origin", host.Server.BaseAddress.GetLeftPart(UriPartial.Authority));
        request.Headers.Add("Sec-Fetch-Site", "same-origin");

        using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_upload_the_browser_says_another_site_made_is_refused()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var sessionId = await SessionAsync(host);
        using var request = Upload(sessionId);
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static HttpRequestMessage Upload(string sessionId)
    {
        var file = new ByteArrayContent([1, 2, 3]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, "/_rask/upload")
        {
            Content = new MultipartFormDataContent { { file, "f0", "data.bin" }, { new StringContent("0"), "f0__lastModified" } },
        };
        request.Headers.Add("Rask-Session", sessionId);
        return request;
    }

    private static async Task<string> SessionAsync(RaskTestHost host)
    {
        using var response = await host.Http.GetAsync("/", TestContext.Current.CancellationToken);
        return MarkupAssert.SessionId(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
