#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

// RenderResult lets Render()/Head bodies use a collection expression ([a, b]) in place of
// Fragment()[a, b], while single-component bodies keep working via the implicit
// Component -> RenderResult conversion. These tests pin both behaviours and prove the
// collection-expression form is byte-identical to the Fragment-wrapped form it replaces.
public class RenderResultTests
{
    [Fact]
    public void Render_CollectionExpression_MatchesFragmentForm()
    {
        var viaCollection = new CollectionRoot().ToHtml();
        var viaFragment = Fragment()[Doctype(), Html()[Body()["hi"]]].ToHtml();

        Assert.Equal("<!DOCTYPE html><html><body>hi</body></html>", viaCollection);
        Assert.Equal(viaFragment, viaCollection);
    }

    [Fact]
    public void Render_SingleComponent_ImplicitConversion_StillRenders() =>
        Assert.Equal("<div>plain</div>", new SingleRoot().ToHtml());

    [Fact]
    public void Render_CollectionExpressionWithText_HtmlEncodesLikeFragment()
    {
        var viaCollection = new TextRoot().ToHtml();
        Assert.Equal("a&lt;b<span></span>", viaCollection);
        Assert.Equal(Fragment()[Text("a<b"), Span()].ToHtml(), viaCollection);
    }

    [Fact]
    public void Head_CollectionExpression_FlattensIntoHead()
    {
        // A collection-expression Head contributes multiple top-level tags; the registry
        // flattens the wrapping Fragment so each tag splices into <head> independently —
        // exactly as Fragment()[Title, Meta] did.
        var html = new HeadShell(new MultiHeadContributor()).RenderAsLiveRoot();

        Assert.DoesNotContain("__rask_head_assets__", html);
        Assert.Contains(">Collected</title>", html);
        Assert.Contains("name=\"description\"", html);
        // Both land inside <head>, before <body>.
        var bodyIdx = html.IndexOf("<body", StringComparison.Ordinal);
        Assert.True(html.IndexOf("<title>", StringComparison.Ordinal) < bodyIdx);
        Assert.True(html.IndexOf("name=\"description\"", StringComparison.Ordinal) < bodyIdx);
    }

    [Fact]
    public void Head_Default_NoContribution_StripsSentinel()
    {
        // Base Head => default means "no contribution"; the sentinel is stripped, same as
        // the old `Head => null`.
        var html = new HeadShell(new NoHeadContributor()).RenderAsLiveRoot();
        Assert.DoesNotContain("__rask_head_assets__", html);
    }

    private sealed class CollectionRoot : Component
    {
        protected override RenderResult Render() => [Doctype(), Html()[Body()["hi"]]];
    }

    private sealed class TextRoot : Component
    {
        protected override RenderResult Render() => [Text("a<b"), Span()];
    }

    private sealed class SingleRoot : Component
    {
        protected override RenderResult Render() => Div()["plain"];
    }

    private sealed class MultiHeadContributor : Component
    {
        protected override RenderResult Head => [Title()["Collected"], Meta(Name: "description", Content: "d")];
        protected override RenderResult Render() => Div()["body"];
    }

    private sealed class NoHeadContributor : Component
    {
        protected override RenderResult Render() => Div()["body"];
    }

    private sealed class HeadShell : Component
    {
        private readonly Component _body;
        public HeadShell(Component body) => _body = body;

        protected override RenderResult Render() =>
            [Doctype(), Html("en")[Head(), Body()[_body]]];
    }
}
