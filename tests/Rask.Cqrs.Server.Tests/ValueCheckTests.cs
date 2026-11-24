using System.Net;
using System.Text;
using System.Text.Json;

namespace Rask.Cqrs.Server.Tests;

// What a browser sends is untrusted: a value object in it is made through its rules as the server reads the message,
// so a handler is never given one they refuse — the caller is answered 400 with the field it is about. One class, so
// its two tests never race over the handler's one record of what it received.
public sealed class ValueCheckTests
{
    [Fact]
    public async Task A_value_object_its_rules_refuse_is_answered_400_under_its_field_and_never_reaches_the_handler()
    {
        RenameDriverHandler.Received = null;
        using var host = EndpointSecurityTests.Host();
        using var client = host.CreateClient();
        using var request = Rename("""{"id":1,"driver":{"name":"far too long a name"}}""");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Validation failed", json.RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("errors").GetProperty("Driver.Name")[0].GetString()));
        Assert.Null(RenameDriverHandler.Received);
    }

    [Fact]
    public async Task A_value_object_its_rules_keep_reaches_the_handler_as_create_makes_it()
    {
        RenameDriverHandler.Received = null;
        using var host = EndpointSecurityTests.Host();
        using var client = host.CreateClient();
        using var request = Rename("""{"id":1,"driver":{"name":"  Ada  "}}""");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, response.StatusCode.ToString());
        Assert.Equal(DriverName.Create("Ada"), RenameDriverHandler.Received);
    }

    private static HttpRequestMessage Rename(string body)
    {
        var request = EndpointSecurityTests.Request(HttpMethod.Post, "Rask.Cqrs.Server.Tests.RenameDriver", authenticated: true);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }
}
