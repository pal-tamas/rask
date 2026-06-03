using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public class RootShellValidationTests
{
    private static IServiceProvider Services() => RenderHarness.EmptyServices();

    [Fact]
    public void RootErrorBoundary_AppMissingShell_ThrowsActionable()
    {
        // The real hosts wrap the app in RootErrorBoundary before RenderAsLiveRoot — that's
        // the gated path the shell check runs on.
        var root = new RootErrorBoundary(new StubComponent(() => Div()["hi"]));

        var ex = Assert.Throws<InvalidOperationException>(() => root.RenderAsLiveRoot(Services()));

        Assert.Contains("page shell", ex.Message);
        Assert.Contains("Doctype()", ex.Message);
        Assert.Contains("Body()", ex.Message);
    }

    [Fact]
    public void RootErrorBoundary_FullShell_DoesNotThrow()
    {
        var root = new RootErrorBoundary(new StubComponent(() =>
            Fragment()[Doctype(), Html("en")[Head(), Body()[P()["hi"]]]]));

        var html = root.RenderAsLiveRoot(Services());

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<body>", html);
    }

    [Fact]
    public void DirectRenderAsLiveRoot_NotWrapped_IsExemptFromShellCheck()
    {
        // Direct RenderAsLiveRoot (the unit-test helper path) renders partial trees and must
        // not trip the shell check.
        var html = new StubComponent(() => Div()["hi"]).RenderAsLiveRoot(Services());

        Assert.Equal("<div>hi</div>", html);
    }
}
