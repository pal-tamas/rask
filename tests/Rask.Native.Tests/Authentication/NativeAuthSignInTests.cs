using System.Net;
using System.Security.Claims;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Native.Authentication;

namespace Rask.Native.Tests.Authentication;

// IAuthSignIn is one of the contracts Rask.Core promises on every host, and the native host had none — a
// shared LoginPage(IAuthSignIn) (the shape `rask new` scaffolds) failed DI on the device only.
public sealed class NativeAuthSignInTests
{
    [Fact]
    public async Task SignOut_ClearsTheLocalToken_EvenWithNoServerToTell()
    {
        // A Native + Local app need not have a backend at all; signing out still has to work.
        var tokens = new FakeTokenStore { Token = "jwt" };
        var user = new FakeUserProvider();
        var sut = new NativeAuthSignIn(user, NavigatorFor(), tokens);

        await sut.SignOutAsync();

        Assert.Null(tokens.Token);
        Assert.Equal(1, user.RefreshCount);
    }

    [Fact]
    public async Task SignOut_PostsToTheLogoutEndpoint_WhenTheAppHasAnHttpClient()
    {
        var handler = new RecordingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var sut = new NativeAuthSignIn(new FakeUserProvider(), NavigatorFor(), null, http);

        await sut.SignOutAsync();

        Assert.Equal("https://api.test/auth/logout", handler.LastUri?.ToString());
    }

    // Clearing the device token has to survive the network call failing — a device app is offline far more
    // often than a browser tab, and a half-signed-out state leaves a live token on the device.
    [Fact]
    public async Task SignOut_ClearsTheTokenBeforeTheNetworkCall_SoAFailureStillSignsOutLocally()
    {
        var tokens = new FakeTokenStore { Token = "jwt" };
        var http = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://api.test/") };
        var sut = new NativeAuthSignIn(new FakeUserProvider(), NavigatorFor(), tokens, http);

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => sut.SignOutAsync());

        Assert.Null(tokens.Token);
    }

    [Theory]
    [InlineData("https://evil.test/", "/")]
    [InlineData("//evil.test/", "/")]
    [InlineData(null, "/")]
    [InlineData("/dashboard", "/dashboard")]
    public async Task SignOut_SanitizesTheReturnUrl(string? returnUrl, string expected)
    {
        // A native app reaches these through deep links, so returnUrl is as attacker-reachable here as a
        // query string is on the web.
        var routeState = new RouteState();
        var navigator = new Navigator(routeState);
        var sut = new NativeAuthSignIn(new FakeUserProvider(), navigator);

        // Navigator refuses to navigate outside an event handler; sign-out is always called from one.
        using (navigator.EnterHandler())
        {
            await sut.SignOutAsync(returnUrl);
        }

        Assert.Equal(expected, routeState.Path);
    }

    [Fact]
    public async Task SignIn_Throws_BecauseADeviceCannotMintItsOwnPrincipal()
    {
        var sut = new NativeAuthSignIn(new FakeUserProvider(), NavigatorFor());

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => sut.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity("test"))));

        Assert.Contains("ITokenStore", ex.Message, StringComparison.Ordinal);
    }

    // The other tests don't assert on navigation, so they only need a Navigator that works.
    private static Navigator NavigatorFor()
    {
        var navigator = new Navigator(new RouteState());
        navigator.EnterHandler();
        return navigator;
    }

    private sealed class FakeTokenStore : ITokenStore
    {
        public string? Token { get; set; }

        public ValueTask<string?> GetAsync() => ValueTask.FromResult(Token);

        public ValueTask SetAsync(string token, bool persist)
        {
            Token = token;
            return default;
        }

        public ValueTask ClearAsync()
        {
            Token = null;
            return default;
        }
    }

    private sealed class FakeUserProvider : IUserProvider
    {
        public int RefreshCount { get; private set; }
        public ClaimsPrincipal Current { get; } = new(new ClaimsIdentity());

        public event Action? Changed;

        public Task RefreshAsync()
        {
            RefreshCount++;
            Changed?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("offline");
    }
}
