using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

/// <summary>
///     The framework owns the document: an app renders into <c>&lt;body&gt;</c> and
///     <see cref="RootErrorBoundary" /> — the wrapper every host installs — builds the doctype,
///     <c>&lt;html&gt;</c>, <c>&lt;head&gt;</c> and <c>&lt;body&gt;</c> around it.
/// </summary>
public partial class RootShellCompositionTests : global::Rask.Core.RaskMarkup
{
    private static IServiceProvider Services() => RenderHarness.EmptyServices();

    private static string RenderApp(Component app) =>
        new RootErrorBoundary(app).RenderAsLiveRoot(Services());

    [Fact]
    public void An_app_that_renders_only_its_body_still_gets_a_whole_document()
    {
        var html = RenderApp(new StubComponent(() => Div["hi"]));

        Assert.Equal("<!DOCTYPE html><html lang=\"en\"><head></head><body><div>hi</div></body></html>", html);
    }

    [Fact]
    public void An_app_that_renders_nothing_still_gets_a_whole_document()
    {
        var html = RenderApp(new StubComponent(() => null!));

        Assert.Equal("<!DOCTYPE html><html lang=\"en\"><head></head><body></body></html>", html);
    }

    [Fact]
    public void The_apps_head_override_lands_in_the_frameworks_head()
    {
        var html = RenderApp(new HeadApp());

        // The registry stamps its singleton key onto <title> so a later contributor can supersede it.
        Assert.Contains("<head><title data-rask-key=\"tag:title\">from the app</title></head>", html);
    }

    [Fact]
    public void HtmlLang_and_BodyClass_stamp_their_elements()
    {
        var html = RenderApp(new AttributedApp());

        Assert.StartsWith("<!DOCTYPE html><html lang=\"de\">", html);
        Assert.Contains("<body class=\"dark\">", html);
    }

    [Fact]
    public void A_null_HtmlLang_omits_the_attribute_entirely()
    {
        var html = RenderApp(new NoLangApp());

        Assert.StartsWith("<!DOCTYPE html><html>", html);
    }

    /// <summary>
    ///     The escape hatch takes the pieces as parameters, so an override never has to name the
    ///     <c>Head()</c> tag — which is what keeps it clear of the <see cref="Component.HeadAssets" /> virtual.
    /// </summary>
    [Fact]
    public void A_shell_override_composes_the_document_itself()
    {
        var html = RenderApp(new CustomShellApp());

        Assert.Equal(
            "<!DOCTYPE html><html lang=\"en\" dir=\"rtl\"><head></head>"
            + "<body id=\"app\"><main><div>hi</div></main></body></html>",
            html);
    }

    /// <summary>
    ///     A <c>Shell</c> override is user code, so it is held to the same promise as a <c>Render()</c>:
    ///     a throw shows the error page instead of escaping to the host. The framework's own default
    ///     shell takes over for that render — the error page still needs a document to live in.
    /// </summary>
    [Fact]
    public void A_shell_that_throws_shows_the_error_page_inside_the_default_shell()
    {
        var root = new RootErrorBoundary(new ThrowingShellApp());

        var html = root.RenderAsLiveRoot(Services());

        Assert.StartsWith("<!DOCTYPE html><html lang=\"en\">", html);
        Assert.Contains("Something went wrong", html);
        Assert.Contains("shell went wrong", html);
        Assert.True(root.RenderedFallback);
    }

    /// <summary>
    ///     A fault leaves the document standing — the app's shell, and a head complete enough to read:
    ///     the error page contributes its own charset, viewport and title, because the App that threw
    ///     contributed none.
    /// </summary>
    [Fact]
    public void An_app_that_throws_keeps_the_document_and_gets_the_error_pages_head()
    {
        var html = RenderApp(new ThrowingApp());

        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("charset=\"utf-8\"", html);
        Assert.Contains(">Application error</title>", html);
        Assert.Contains("Something went wrong", html);
    }

    /// <summary>
    ///     A <em>nested</em> boundary's fallback replaces one widget while the rest of the page is fine,
    ///     so it says nothing about the document — retitling the tab "Application error" because a sidebar
    ///     failed would be a worse lie than the missing title the root fallback's head exists to fix.
    /// </summary>
    [Fact]
    public void A_nested_fallback_replaces_its_widget_without_retitling_the_page()
    {
        var html = RenderApp(new NestedFailureApp());

        Assert.Contains(">still fine</title>", html);
        Assert.DoesNotContain("Application error", html);
        Assert.Contains("Something went wrong", html);
    }

    /// <summary>
    ///     Rendering a component directly — the unit-test helper path — composes no shell. The document
    ///     is the host's, and a partial tree renders as exactly itself.
    /// </summary>
    [Fact]
    public void Rendering_directly_as_the_live_root_composes_no_shell()
    {
        var html = new StubComponent(() => Div["hi"]).RenderAsLiveRoot(Services());

        Assert.Equal("<div>hi</div>", html);
    }

    private sealed class HeadApp : Component
    {
        protected override Component? HeadAssets => Title["from the app"];
        protected override Component? Render() => null;
    }

    private sealed class AttributedApp : Component
    {
        protected override string? HtmlLang => "de";
        protected override string? BodyClass => "dark";
        protected override Component? Render() => null;
    }

    private sealed class NoLangApp : Component
    {
        protected override string? HtmlLang => null;
        protected override Component? Render() => null;
    }

    private sealed class CustomShellApp : Component
    {
        protected override Component Shell(Component head, Component body) =>
            Html.Lang("en").Dir("rtl")[head, Body.Id("app")[Main[body]]];

        protected override Component? Render() => Div["hi"];
    }

    private sealed class ThrowingShellApp : Component
    {
        protected override Component Shell(Component head, Component body) =>
            throw new InvalidOperationException("shell went wrong");

        protected override Component? Render() => Div["hi"];
    }

    private sealed class ThrowingApp : Component
    {
        protected override Component? Render() => throw new InvalidOperationException("render went wrong");
    }

    private sealed class NestedFailureApp : Component
    {
        protected override Component? HeadAssets => Title["still fine"];

        protected override Component? Render()
        {
            var boundary = new ErrorBoundary();
            boundary.SetProps([new ThrowingApp()], null);
            return boundary;
        }
    }
}
