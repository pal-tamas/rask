using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

public class RuntimeScriptEndpointTests
{
    [Fact]
    public async Task Get_RaskJs_ReturnsEmbeddedScriptWithJavaScriptContentType()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/rask/rask.js");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);
    }
}
