using Rask.Server.Prerender;

namespace Rask.Server.Tests.Prerender;

// What may become the copy of a public page that everyone is served. The renders judged here are
// neutral — made for nobody — so the question is only whether a render is finished and ordinary, and
// whether a failure is the kind that should keep the copy a page already has.
public class PageStorePolicyTests
{
    private const string Document = "<html><body><p>x</p></body></html>";
    private const long Budget = 1024;

    [Fact]
    public void AFinishedOrdinaryPage_IsStored()
    {
        var verdict = PageStorePolicy.Decide(Render(), Document, Document.Length, Budget);

        Assert.Equal(PageStoreDecision.Store, verdict.Decision);
        Assert.Empty(verdict.Reason);
    }

    [Fact]
    public void APageThatThrew_KeepsTheCopyItHad()
    {
        // An outage must not erase good content: the old copy is still the right page to serve.
        AssertKeepsStale(Render(faulted: true, status: 500));
    }

    [Fact]
    public void APageThatDidNotSettle_KeepsTheCopyItHad()
    {
        // Its markup is a placeholder, and a stored spinner is worse than no copy, because it looks like
        // the page.
        AssertKeepsStale(Render(timedOut: true));
    }

    [Fact]
    public void APageWaitingOnJavaScript_KeepsTheCopyItHad()
    {
        AssertKeepsStale(Render(blockedOnJs: true));
    }

    [Fact]
    public void AServerError_KeepsTheCopyItHad()
    {
        AssertKeepsStale(Render(status: 503));
    }

    [Fact]
    public void AFaultIsJudgedBeforeTheStatusItCarries()
    {
        // A crashed page answers 500; which rule names it matters for the log, not the decision — but the
        // fault is the more useful thing to have said.
        var verdict = PageStorePolicy.Decide(Render(faulted: true, status: 500), Document, Document.Length, Budget);

        Assert.Contains("threw", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ARedirect_IsEvicted()
    {
        AssertEvicted(Render(kind: PageRenderKind.Redirect, status: 302));
    }

    [Theory]
    [InlineData(404)] // a deleted product must stop being served, not keep its old page
    [InlineData(410)]
    [InlineData(204)]
    public void AnythingButA200_IsEvicted(int status)
    {
        AssertEvicted(Render(status: status));
    }

    [Fact]
    public void ARenderThatSignsSomeoneIn_IsEvicted()
    {
        // A neutral render is made for nobody, so being signed in afterwards means the render did it — and
        // whatever it shows now belongs to that person.
        AssertEvicted(Render(authenticated: true));
    }

    [Fact]
    public void APageThatNeedsASession_IsEvicted_WithTheReasonSaid()
    {
        var verdict = PageStorePolicy.Decide(Render(needsSession: true), document: null, 0, Budget);

        Assert.Equal(PageStoreDecision.Evict, verdict.Decision);
        Assert.Contains("live session", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentWhoseScriptCouldNotBeRemoved_IsEvicted()
    {
        AssertEvicted(Render(), document: null);
    }

    [Fact]
    public void ACopyLargerThanTheBudget_IsEvicted()
    {
        var verdict = PageStorePolicy.Decide(Render(), Document, (int)Budget + 1, Budget);

        Assert.Equal(PageStoreDecision.Evict, verdict.Decision);
    }

    private static void AssertKeepsStale(PageRenderResult render)
    {
        var verdict = PageStorePolicy.Decide(render, Document, Document.Length, Budget);

        Assert.Equal(PageStoreDecision.KeepStale, verdict.Decision);
        Assert.NotEmpty(verdict.Reason);
    }

    private static void AssertEvicted(PageRenderResult render, string? document = Document)
    {
        var verdict = PageStorePolicy.Decide(render, document, document?.Length ?? 0, Budget);

        Assert.Equal(PageStoreDecision.Evict, verdict.Decision);
        Assert.NotEmpty(verdict.Reason);
    }

    private static PageRenderResult Render(
        PageRenderKind kind = PageRenderKind.Rendered,
        int status = 200,
        bool needsSession = false,
        bool faulted = false,
        bool timedOut = false,
        bool blockedOnJs = false,
        bool authenticated = false) =>
        new(
            kind,
            Document,
            RedirectLocation: kind == PageRenderKind.Redirect ? "/elsewhere" : null,
            status,
            needsSession,
            DeclaredStatic: false,
            faulted,
            timedOut,
            blockedOnJs,
            authenticated,
            ReadsUser: false);
}
