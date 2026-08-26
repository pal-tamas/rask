using System.Net;
using Rask.Core;
using Rask.Server.Http;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Endpoints;

// A page with no handler, no form, no ref and no JS call is inert once it reaches the browser. It
// still cost a DI scope, a component tree held for ten seconds, a slot against MaxSessions and a
// socket — and a `no-store` header that put it beyond every cache, including the browser's own
// back/forward. This is that page served as what it already was: a document.
public class StaticPageTests
{
    [Fact]
    public async Task AStaticPage_KeepsNoSession()
    {
        using var host = Host<ContentOnlyApp>();

        var response = await host.Http.GetAsync("/");
        response.EnsureSuccessStatusCode();

        // Assert the counts, not just the body: a session that is merely invisible in the HTML is
        // still a DI scope and a component tree pinned against the cap.
        Assert.Equal(0, host.Store.Count);
        Assert.Equal(0, host.Store.LiveCount);
    }

    [Fact]
    public async Task AStaticPage_ShipsNoRuntimeAndNoSessionId()
    {
        using var host = Host<ContentOnlyApp>();

        var body = await host.Http.GetStringAsync("/");

        Assert.DoesNotContain("/rask/rask.js", body);
        Assert.DoesNotContain("data-rask-root", body);
        // The content is still all there — this is the same render, minus what it never needed.
        Assert.Contains("just-content", body);
    }

    [Fact]
    public async Task AnAnonymousStaticPage_BecomesCacheable()
    {
        using var host = Host<ContentOnlyApp>();

        var response = await host.Http.GetAsync("/");

        // Dropping no-store is the user-visible win: it restores bfcache and instant back/forward.
        // `private` keeps every shared cache out, and Vary: Cookie stops a cache serving the
        // logged-out page to a signed-in user, since "anonymous" is itself a function of the cookie.
        // Asserted on the parsed directives, not the header text: HttpClient reorders them, and a
        // substring check for "no-store" would in any case be satisfied by the word inside another.
        var cache = response.Headers.CacheControl;
        Assert.NotNull(cache);
        Assert.False(cache!.NoStore);
        Assert.True(cache.Private);
        Assert.True(cache.MustRevalidate);
        Assert.Equal(TimeSpan.Zero, cache.MaxAge);
        Assert.Contains("Cookie", response.Headers.Vary);
        // Pragma is HTTP/1.0 and only meaningful alongside no-store; carrying it here would be noise.
        Assert.Empty(response.Headers.Pragma);
    }

    [Theory]
    [InlineData(typeof(HandlerApp))]
    [InlineData(typeof(RefApp))]
    public async Task APageThatNeedsAConnection_KeepsItsSession(Type appType)
    {
        using var host = appType == typeof(HandlerApp) ? Host<HandlerApp>() : Host<RefApp>();

        var response = await host.Http.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(1, host.Store.Count);
        Assert.Contains("data-rask-root", body);
        Assert.Contains("/rask/rask.js", body);
        // Still beyond every cache: this body carries the session id.
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task APageWhoseDataNeverArrives_KeepsItsSession()
    {
        // The timeout case. Served as a static document it would sit on its placeholder for ever,
        // because nothing would be left running that could replace it.
        using var host = RaskTestHost.Create<NeverSettlesStaticApp>(configureServer: o =>
        {
            o.StaticPages = true;
            o.InitialRenderQuiescenceTimeout = TimeSpan.FromMilliseconds(100);
        });

        var body = await host.Http.GetStringAsync("/");

        Assert.Equal(1, host.Store.Count);
        Assert.Contains("data-rask-root", body);
    }

    [Fact]
    public async Task WithTheFeatureOff_NothingChanges()
    {
        using var host = RaskTestHost.Create<ContentOnlyApp>();

        var response = await host.Http.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(1, host.Store.Count);
        Assert.Contains("data-rask-root", body);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    // The splice must fail closed. A document whose runtime tag is not exactly where it belongs is
    // one we decline to serve as cacheable, rather than guessing.
    [Theory]
    [InlineData("<html><body><p>x</p></body></html>")]                                   // no tag
    [InlineData("<html><body><script src=\"/rask/rask.js\"></script><p>x</p></body></html>")] // not last
    public void TheSplice_DeclinesWhatItDoesNotRecognise(string html)
    {
        Assert.Null(RuntimeScriptSplice.TryRemove(html, string.Empty));
    }

    [Fact]
    public void TheSplice_RemovesTheTagItRecognises()
    {
        const string html = "<html><body><p>x</p><script src=\"/rask/rask.js\"></script></body></html>";

        Assert.Equal("<html><body><p>x</p></body></html>", RuntimeScriptSplice.TryRemove(html, string.Empty));
    }

    private static RaskTestHost Host<TApp>() where TApp : Component =>
        RaskTestHost.Create<TApp>(configureServer: o => o.StaticPages = true);
}

public sealed partial class ContentOnlyApp : Component
{
    protected override Component? HeadAssets => Title["content-only"];

    protected override Component? Render() => Div[P["just-content"], A.Href("/elsewhere")["a link"]];
}

public sealed partial class HandlerApp : Component
{
    private int _count;

    protected override Component? HeadAssets => Title["handler"];

    protected override Component? Render() => Button.OnClick(() => _count++)[$"clicked {_count}"];
}

public sealed partial class RefApp : Component
{
    private readonly ElementRef _chart = ElementRef.New();

    protected override Component? HeadAssets => Title["ref"];

    // No handler anywhere — a ref exists to be handed to JavaScript, and that needs a connection.
    protected override Component? Render() => Div.Ref(_chart)["chart-host"];
}

public sealed partial class NeverSettlesStaticApp : Component
{
    private string? _value;

    protected override Component? HeadAssets => Title["never-settles-static"];

    protected override async Task OnMountAsync()
    {
        await new TaskCompletionSource().Task;
        _value = "never";
    }

    protected override Component? Render() => Div[_value ?? "still-loading"];
}
