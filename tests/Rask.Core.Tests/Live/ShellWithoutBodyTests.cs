using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// #1234. A Shell override that places `body` straight into Html, without a Body element, used to render a
// page that painted and was dead: the runtime <script> is written at </body>, and the session id the
// runtime connects with is stamped on <body>, so with no body neither was written and nothing said so.
//
// In the ConsoleRedirect collection because these tests swap the process-wide diagnostics sink. The head is
// not asserted: scoped assets other test classes register land in it.
[Collection("ConsoleRedirect")]
public partial class ShellWithoutBodyTests : global::Rask.Core.RaskMarkup
{
    private const string ScriptHtml = "<script src=\"/rask/rask.js\"></script>";

    [Fact]
    public void A_shell_without_a_body_element_gets_one_around_the_app_and_the_runtime_script_inside_it()
    {
        using var services = WithRuntime();

        var html = new RootErrorBoundary(new NoBodyShellApp()).RenderAsLiveRoot(services);

        Assert.StartsWith("<!DOCTYPE html><html lang=\"hu\"><head>", html);
        Assert.EndsWith("</head><body><div>hi</div>" + ScriptHtml + "</body></html>", html);
    }

    [Fact]
    public void A_shell_that_nests_the_app_without_a_body_element_gets_one_around_everything_after_the_head()
    {
        using var services = WithRuntime();

        var html = new RootErrorBoundary(new NestedNoBodyShellApp()).RenderAsLiveRoot(services);

        Assert.EndsWith("</head><body><main><div>hi</div></main>" + ScriptHtml + "</body></html>", html);
    }

    [Fact]
    public void A_shell_without_a_body_element_is_reported_as_a_warning_naming_Shell()
    {
        using var services = WithRuntime();
        RaskDiagnostics.ResetReportOnceForTests();

        var reported = Reported(() => new RootErrorBoundary(new NoBodyShellApp()).RenderAsLiveRoot(services));

        var entry = Assert.Single(reported, e => e.Message.Contains(nameof(NoBodyShellApp), StringComparison.Ordinal));
        Assert.Equal(RaskLogLevel.Warning, entry.Level);
        Assert.Contains("Shell", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Body[body]", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shell_with_its_body_element_is_left_exactly_as_written()
    {
        using var services = WithRuntime();
        RaskDiagnostics.ResetReportOnceForTests();

        var reported = Reported(() => new RootErrorBoundary(new WithBodyShellApp()).RenderAsLiveRoot(services));
        var html = new RootErrorBoundary(new WithBodyShellApp()).RenderAsLiveRoot(services);

        Assert.EndsWith("</head><body id=\"app\"><div>hi</div>" + ScriptHtml + "</body></html>", html);
        Assert.DoesNotContain(reported, e => e.Message.Contains(nameof(WithBodyShellApp), StringComparison.Ordinal));
    }

    [Fact]
    public void A_document_that_still_writes_no_body_is_reported_as_an_error_naming_Shell()
    {
        using var services = WithRuntime();
        RaskDiagnostics.ResetReportOnceForTests();

        var reported = Reported(() => new RootErrorBoundary(new OpaqueNoBodyShellApp()).RenderAsLiveRoot(services));

        var entry = Assert.Single(reported, e => e.Message.Contains("no <body>", StringComparison.Ordinal));
        Assert.Equal(RaskLogLevel.Error, entry.Level);
        Assert.Contains("Shell", entry.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider WithRuntime() =>
        new ServiceCollection()
            .AddSingleton<IRaskRuntimeScript>(new StubRuntimeScript())
            .BuildServiceProvider();

    private static List<RaskDiagnosticEvent> Reported(Action act)
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = e =>
        {
            lock (captured)
            {
                captured.Add(e);
            }
        };
        try
        {
            act();
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }

        return captured;
    }

    private sealed class StubRuntimeScript : IRaskRuntimeScript
    {
        public Component Render() => Raw.Value(ScriptHtml);
    }

    // The issue's shape: the parameter named `body` reads as if it were the element.
    private sealed class NoBodyShellApp : Component
    {
        protected override Component Shell(Component head, Component body) => Html.Lang("hu")[head, body];

        protected override Component? Render() => Div["hi"];
    }

    private sealed class NestedNoBodyShellApp : Component
    {
        protected override Component Shell(Component head, Component body) => Html.Lang("en")[head, Main[body]];

        protected override Component? Render() => Div["hi"];
    }

    private sealed class WithBodyShellApp : Component
    {
        protected override Component Shell(Component head, Component body) =>
            Html.Lang("en")[head, Body.Id("app")[body]];

        protected override Component? Render() => Div["hi"];
    }

    // A component the shell places under <html> could write the <body> itself, so the composition cannot
    // tell before it renders and leaves the document alone; this one writes none, which only the serializer
    // can see.
    private sealed class OpaqueNoBodyShellApp : Component
    {
        protected override Component Shell(Component head, Component body) =>
            Html.Lang("en")[head, new StubComponent(() => body)];

        protected override Component? Render() => Div["hi"];
    }
}
