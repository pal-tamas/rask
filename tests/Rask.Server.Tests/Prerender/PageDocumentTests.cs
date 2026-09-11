using Rask.Server.Prerender;

namespace Rask.Server.Tests.Prerender;

// The one place per-response bytes are shaped from a render. A stored copy and a live response differ
// only in what is stamped on and what is removed, so both shapes are pinned against the same render.
public class PageDocumentTests
{
    private const string WithRuntime =
        "<html><head></head><body><p>content</p><script src=\"/rask/rask.js\"></script></body></html>";

    [Fact]
    public void ADocumentPage_IsBakedWithoutItsRuntimeScript()
    {
        // Byte for byte what a live request for a static page is sent: the same render, minus the script
        // that only a session could have used.
        Assert.Equal(
            "<html><head></head><body><p>content</p></body></html>",
            PageDocument.Baked(Render(WithRuntime, needsSession: false), pathBase: string.Empty));
    }

    [Fact]
    public void APageThatNeedsASession_IsNotBaked()
    {
        // Served without one it would sit on screen with nothing to answer its handlers.
        Assert.Null(PageDocument.Baked(Render(WithRuntime, needsSession: true), pathBase: string.Empty));
    }

    [Fact]
    public void ARuntimeScriptOutOfPlace_IsNotBaked()
    {
        // The splice fails closed: a document that might still carry the runtime is never shared.
        const string misplaced =
            "<html><body><script src=\"/rask/rask.js\"></script><p>content</p></body></html>";

        Assert.Null(PageDocument.Baked(Render(misplaced, needsSession: false), pathBase: string.Empty));
    }

    [Fact]
    public void TheSpliceUsesThePathBaseTheScriptWasRenderedWith()
    {
        const string underBase =
            "<html><body><p>content</p><script src=\"/sub/rask/rask.js\"></script></body></html>";

        Assert.Equal(
            "<html><body><p>content</p></body></html>",
            PageDocument.Baked(Render(underBase, needsSession: false), pathBase: "/sub"));
    }

    [Fact]
    public void ALiveDocument_CarriesItsSessionAndTheBundleItCanMoveInto()
    {
        var live = PageDocument.Live(
            WithRuntime, "session-1", new RaskServerLimits { WasmBundleUrl = "/main.js" }, dev: false);

        Assert.Contains("data-rask-root=\"session-1\"", live, StringComparison.Ordinal);
        // Suffix only: the URL carries whatever PathBase a concurrently running test has set.
        Assert.Contains("/main.js\"", live, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-dev", live, StringComparison.Ordinal);
    }

    private static PageRenderResult Render(string html, bool needsSession) =>
        new(
            PageRenderKind.Rendered,
            html,
            RedirectLocation: null,
            StatusCode: 200,
            needsSession,
            DeclaredStatic: false,
            Faulted: false,
            TimedOut: false,
            BlockedOnJs: false,
            Authenticated: false,
            ReadsUser: false);
}
