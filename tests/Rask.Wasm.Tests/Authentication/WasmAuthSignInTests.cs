using System.Net;
using System.Security.Claims;
using Microsoft.JSInterop;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Wasm.Authentication;
using Rask.Wasm.Tests.Browser;

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
    public async Task Signing_out_sanitizes_the_return_url_before_navigating(string? returnUrl, string expectedPath)
    {
        var state = new RouteState();
        var nav = new Navigator(state);
        var auth = new WasmAuthSignIn(StubHttp(), new StubUserProvider(), new FakeJsRuntime());

        using (nav.EnterHandler())
        {
            await auth.SignOut(returnUrl);
        }

        Assert.Equal(expectedPath, state.Path);
    }

    [Fact]
    public async Task Signing_out_empties_the_offline_cache()
    {
        // #1184: what the service worker kept, it kept for whoever was signed in.
        var js = new FakeJsRuntime();
        var auth = new WasmAuthSignIn(StubHttp(), new StubUserProvider(), js);

        using (new Navigator(new RouteState()).EnterHandler())
        {
            await auth.SignOut();
        }

        Assert.Equal(1, js.CallCount("__raskOffline.clear"));
    }

    [Fact]
    public async Task Signing_out_still_navigates_when_the_page_has_no_cache_helper()
    {
        var js = new FakeJsRuntime();
        js.SetException("__raskOffline.clear", new JSException("__raskOffline is not defined"));
        var state = new RouteState();
        var auth = new WasmAuthSignIn(StubHttp(), new StubUserProvider(), js);

        using (new Navigator(state).EnterHandler())
        {
            await auth.SignOut("/bye");
        }

        Assert.Equal("/bye", state.Path);
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
        public event EventHandler? Changed { add { } remove { } }
    }
}
