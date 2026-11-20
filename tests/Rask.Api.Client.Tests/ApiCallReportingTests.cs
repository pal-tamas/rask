using System.Net;

namespace Rask.Api.Client.Tests;

/// <summary>What a failed call says about where it went.</summary>
public sealed class ApiCallReportingTests
{
    [Fact]
    public async Task A_failed_call_names_its_path_without_the_query_it_carried()
    {
        using var http = new HttpClient(new Answering(HttpStatusCode.InternalServerError))
        {
            BaseAddress = new Uri("https://api.example.com/"),
        };

        var error = await Assert.ThrowsAsync<ApiException>(() => ApiCall.Send(
            http, new ApiClientOptions(), HttpMethod.Get, "people?email=ada%40example.com", body: null,
            TestContext.Current.CancellationToken));

        Assert.EndsWith("/people", error.Path, StringComparison.Ordinal);
        Assert.DoesNotContain("ada", error.Message, StringComparison.Ordinal);
    }

    private sealed class Answering(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}
