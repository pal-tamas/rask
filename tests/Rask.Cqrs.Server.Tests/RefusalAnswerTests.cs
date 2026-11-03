using System.Net;
using System.Text;
using System.Text.Json;

namespace Rask.Cqrs.Server.Tests;

// A handler that refuses — RefusedException, the app saying no in a sentence written for the caller — is a 400
// that says it, with the `errors` a client's form places, and never the 500 a fault is.
public sealed class RefusalAnswerTests
{
    [Fact]
    public async Task A_refusing_handler_answers_400_with_its_message_as_the_detail_and_under_the_empty_key()
    {
        using var host = EndpointSecurityTests.Host();
        using var client = host.CreateClient();
        using var request = EndpointSecurityTests.Request(HttpMethod.Post, "Rask.Cqrs.Server.Tests.Refuses", authenticated: true);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("The invoice is already closed.", json.RootElement.GetProperty("detail").GetString());
        Assert.Equal("The invoice is already closed.", json.RootElement.GetProperty("errors").GetProperty("")[0].GetString());
        Assert.False(json.RootElement.TryGetProperty("reference", out _));
    }
}
