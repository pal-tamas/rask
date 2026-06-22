using System.Net;
using System.Security.Claims;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Wasm.Authentication;

namespace Rask.Wasm.Tests.Authentication;

// WASM sign-out SPA-navigates to returnUrl, which is whatever the caller passed (commonly a
// ?returnUrl= query value an attacker can shape). NavigateTo can leave the origin, so the
// implementation must collapse anything non-local to "/" via the shared LocalUrl rule — the same
// open-redirect guard the server sign-in path applies — before navigating.
public class WasmAuthSignInTests
{
    [Theory]
    [InlineData("/dashboard", "/dashboard")]
    [InlineData("/a/b?x=1", "/a/b?x=1")]
    [InlineData(null, "/")]
    [InlineData("//evil.com", "/")]       // protocol-relative
    [InlineData("/\\evil.com", "/")]      // backslash → browser normalises to "//"
    [InlineData("\\evil.com", "/")]
    [InlineData("https://evil.com", "/")] // absolute URL
    [InlineData("evil.com", "/")]         // not rooted
    public async Task SignOut_SanitizesReturnUrl_BeforeNavigating(string? returnUrl, string expectedPath)
    {
        var state = new RouteState();
        var nav = new Navigator(state);
        var auth = new WasmAuthSignIn(StubHttp(), new StubUserProvider(), nav);

        using (nav.EnterHandler())
        {
            await auth.SignOutAsync(returnUrl);
        }

        Assert.Equal(expectedPath, state.Path);
    }

    private static HttpClient StubHttp() =>
        new(new OkHandler()) { BaseAddress = new Uri("http://localhost/") };

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class StubUserProvider : IUserProvider
    {
        public ClaimsPrincipal Current { get; } = new(new ClaimsIdentity());
        public event Action? Changed { add { } remove { } }
    }
}
