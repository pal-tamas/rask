using Microsoft.Extensions.DependencyInjection;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public partial class RuntimeScriptInjectionTests : global::Rask.Core.RaskMarkup
{
    private const string ScriptHtml = "<script src=\"/rask/rask.js\"></script>";

    private static ServiceProvider WithProvider() =>
        new ServiceCollection()
            .AddSingleton<IRaskRuntimeScript>(new StubRuntimeScriptProvider(Raw.Value(ScriptHtml)))
            .BuildServiceProvider();

    private static Component Shell(params Component[] bodyChildren) =>
        [Doctype, Html.Lang("en")[Head, Body[bodyChildren]]];

    [Fact]
    public void Body_ProviderRegistered_InjectsScriptAsLastBodyChild()
    {
        var view = new StubComponent(() => Shell(P["hi"]));

        var html = view.RenderAsLiveRoot(WithProvider());

        // Script is auto-injected even though the tree never mentions RaskRuntimeScript().
        Assert.Contains(ScriptHtml + "</body>", html);
    }

    [Fact]
    public void Body_NoProvider_InjectsNothing()
    {
        var view = new StubComponent(() => Shell(P["hi"]));

        var html = view.RenderAsLiveRoot(RenderHarness.EmptyServices());

        Assert.DoesNotContain("rask.js", html);
        Assert.Contains("<p>hi</p></body>", html);
    }

    [Fact]
    public void Body_LegacyRaskRuntimeScriptStillInTree_EmitsExactlyOneScript()
    {
        // RaskRuntimeScript() is a no-op; the framework injects one script at body close.
        var view = new StubComponent(() => Shell(P["hi"], RaskRuntimeScript));

        var html = view.RenderAsLiveRoot(WithProvider());

        var first = html.IndexOf(ScriptHtml, StringComparison.Ordinal);
        Assert.True(first >= 0, "expected the injected runtime script");
        Assert.Equal(-1, html.IndexOf(ScriptHtml, first + ScriptHtml.Length, StringComparison.Ordinal));
    }

    [Fact]
    public void NonLiveToHtml_DoesNotInject()
    {
        // Body().ToHtml() outside a live render must stay bare (no provider reachable anyway).
        Assert.Equal("<body></body>", Body.ToHtml());
    }

    private sealed class StubRuntimeScriptProvider : IRaskRuntimeScript
    {
        private readonly Component _component;
        public StubRuntimeScriptProvider(Component component) => _component = component;
        public Component Render() => _component;
    }
}
