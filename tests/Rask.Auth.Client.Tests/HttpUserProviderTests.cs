using System.Net;
using System.Security.Claims;
using System.Text;
using Rask.Core.Authentication;

namespace Rask.Auth.Client.Tests;

/// <summary>
///     Reading the current user in the browser, over the app's own <c>/api/auth/me</c>.
/// </summary>
public sealed class HttpUserProviderTests
{
    [Fact]
    public async Task Nobody_signed_in_reads_as_anonymous()
    {
        var provider = Provider(HttpStatusCode.NoContent);

        await provider.EnsureLoadedAsync();

        Assert.False(provider.Current.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task A_signed_in_user_carries_a_name_and_its_roles()
    {
        var provider = Provider(
            HttpStatusCode.OK,
            """{"id":"u1","email":"owner@example.com","roles":["admin","user"]}""");

        await provider.EnsureLoadedAsync();

        // IsAuthenticated is the whole point: an identity built without an authentication type reports
        // false, and every [Authorize] check would then treat a signed-in visitor as anonymous.
        Assert.True(provider.Current.Identity?.IsAuthenticated);
        Assert.Equal("owner@example.com", provider.Current.Identity?.Name);
        Assert.True(provider.Current.IsInRole("admin"));
        Assert.Equal("u1", provider.Current.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public async Task Loading_the_user_raises_Changed_so_the_page_re_renders()
    {
        var provider = Provider(HttpStatusCode.OK, """{"id":"u1","email":"a@b.c","roles":[]}""");

        var raised = 0;
        provider.Changed += () => raised++;

        await provider.EnsureLoadedAsync();

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task An_unreachable_endpoint_reads_as_anonymous_rather_than_throwing()
    {
        var provider = new HttpUserProvider(
            new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://localhost") },
            new AuthClientOptions());

        // Anonymous closes doors rather than opening them, and a boot that throws here would take the
        // whole app down for a network blip.
        await provider.EnsureLoadedAsync();

        Assert.False(provider.Current.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task The_user_is_fetched_once_until_a_refresh_asks_again()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, null);
        var provider = new HttpUserProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://localhost") },
            new AuthClientOptions());

        await provider.EnsureLoadedAsync();
        await provider.EnsureLoadedAsync();

        Assert.Equal(1, handler.Calls);

        await provider.RefreshAsync();
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task It_asks_the_configured_prefix()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, null);
        var provider = new HttpUserProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://localhost") },
            new AuthClientOptions { Prefix = "/internal/auth" });

        await provider.EnsureLoadedAsync();

        Assert.Equal("/internal/auth/me", handler.LastPath);
    }

    private static HttpUserProvider Provider(HttpStatusCode status, string? json = null) =>
        new(
            new HttpClient(new StubHandler(status, json)) { BaseAddress = new Uri("https://localhost") },
            new AuthClientOptions());

    private sealed class StubHandler(HttpStatusCode status, string? json) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string? LastPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastPath = request.RequestUri?.AbsolutePath;

            var response = new HttpResponseMessage(status);

            if (json is not null)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no route to host");
    }
}
