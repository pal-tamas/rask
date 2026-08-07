using Rask.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

public class CodeFixProviderTests
{
    private static string App(string body) => $$"""
        using Rask.Core;
        using static Rask.Core.Components.Generated;
        namespace Demo;
        public sealed class App : Component
        {
            protected override Component? Render()
            {
                {{body}}
            }
        }
        """;

    // ---- RASK023: Img missing Alt -> insert Alt: "" ----

    [Fact]
    public async Task Rask023_InsertsEmptyAlt_AfterNamedSrc()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new ImgMissingAltAnalyzer(), new ImgMissingAltCodeFixProvider(), "RASK023",
            App("return Img(Src: \"/a.png\");"));
        Assert.Contains("Img(Src: \"/a.png\", Alt: \"\")", fixhed);
    }

    [Fact]
    public async Task Rask023_InsertsAlt_WhenNoArguments()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new ImgMissingAltAnalyzer(), new ImgMissingAltCodeFixProvider(), "RASK023",
            App("return Img();"));
        Assert.Contains("Img(Alt: \"\")", fixhed);
    }

    [Fact]
    public async Task Rask023_InsertsAlt_AfterPositionalSrc()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new ImgMissingAltAnalyzer(), new ImgMissingAltCodeFixProvider(), "RASK023",
            App("return Img(\"/a.png\");"));
        Assert.Contains("Img(\"/a.png\", Alt: \"\")", fixhed);
    }

    // ---- RASK001: property becomes a required factory param -> add `required` ----

    [Fact]
    public async Task Rask001_AddsRequiredModifier_AfterAccessibility()
    {
        var source = """
            using Rask.Core;
            namespace Demo;
            public sealed class Card : Component
            {
                public string Title { get; set; }
            }
            """;
        var fixhed = await CodeFixHarness.ApplyGeneratorFixAsync(
            new ComponentFactoryGenerator(), new RequiredFactoryParamCodeFixProvider(), "RASK001", source);
        Assert.Contains("public required string Title { get; set; }", fixhed);
    }

    [Fact]
    public async Task Rask001_Offered_WithDIConstructorAndNoParameterless()
    {
        // A DI ctor with no parameterless ctor builds via ActivatorUtilities and post-assigns the prop,
        // so a required no-initializer prop is honored and RASK002 does not fire. The fix stays available.
        var source = """
            using Rask.Core;
            namespace Demo;
            public sealed class Card : Component
            {
                private readonly string _svc;
                public Card(string svc) => _svc = svc;
                public string Title { get; set; }
            }
            """;
        var offered = await CodeFixHarness.IsGeneratorFixOfferedAsync(
            new ComponentFactoryGenerator(), new RequiredFactoryParamCodeFixProvider(), "RASK001", source);
        Assert.True(offered);
    }

    [Fact]
    public async Task Rask001_Offered_WhenDIConstructorHasParameterlessSibling()
    {
        // A parameterless ctor exists alongside the DI ctor, so the factory can `new Card()` + set the
        // property — RASK002 does not fire, so the fix stays available.
        var source = """
            using Rask.Core;
            namespace Demo;
            public sealed class Card : Component
            {
                public Card() { }
                public Card(string svc) { }
                public string Title { get; set; }
            }
            """;
        var offered = await CodeFixHarness.IsGeneratorFixOfferedAsync(
            new ComponentFactoryGenerator(), new RequiredFactoryParamCodeFixProvider(), "RASK001", source);
        Assert.True(offered);
    }

    // ---- RASK014: `new Widget()` -> the generated factory ----
    //
    // A user component rather than a built-in tag: inside a `using static …Generated` scope a tag name
    // binds to the generated factory METHOD, so `new Div()` doesn't resolve to the type there at all.

    private static string Caller(string body) => $$"""
        using Rask.Core;
        namespace Demo;
        public sealed class Widget : Component
        {
            public Widget() { }
            public Widget(string label) { }
            public string? Id { get; set; }
            public override Component? Render() => this;
        }
        class Caller { void M() { {{body}} } }
        """;

    [Fact]
    public async Task Rask014_RewritesArgumentlessNew_ToTheFactoryCall()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new ComponentConstructionAnalyzer(), new ComponentConstructionCodeFixProvider(), "RASK014",
            Caller("var x = new Widget();"));
        Assert.Contains("var x = Widget();", fixhed);
        Assert.DoesNotContain("new Widget()", fixhed);
    }

    [Fact]
    public async Task Rask014_DropsTheQualifier_BecauseTheFactoryIsAMethodNotAType()
    {
        // `new Demo.Widget()` must become `Widget()`, not `Demo.Widget()` — the latter names a type where
        // a method has to go, and would not compile.
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new ComponentConstructionAnalyzer(), new ComponentConstructionCodeFixProvider(), "RASK014",
            Caller("var x = new Demo.Widget();"));
        Assert.Contains("var x = Widget();", fixhed);
    }

    [Fact]
    public async Task Rask014_Withheld_WhenTheConstructionHasArguments()
    {
        // The factory's parameters are generated from the component's public properties, in an order that
        // is not the constructor's. Carrying positional arguments across would compile and mean something
        // else — worse than leaving the error standing with its (already actionable) message.
        var offered = await CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new ComponentConstructionAnalyzer(), new ComponentConstructionCodeFixProvider(), "RASK014",
            Caller("var x = new Widget(\"hi\");"));
        Assert.False(offered);
    }

    [Fact]
    public async Task Rask014_Withheld_WhenAnObjectInitializerIsPresent()
    {
        // An object initializer is only legal after `new`, so it cannot ride along onto a factory call.
        var offered = await CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new ComponentConstructionAnalyzer(), new ComponentConstructionCodeFixProvider(), "RASK014",
            Caller("var x = new Widget { Id = \"a\" };"));
        Assert.False(offered);
    }

    // ---- RASK026: a StateHasChanged() the framework already does -> delete the statement ----

    [Fact]
    public async Task Rask026_DeletesTheRedundantCall()
    {
        var source = """
            using Rask.Core;
            using static Rask.Core.Components.Generated;
            namespace Demo;
            public sealed class App : Component
            {
                private int _n;
                protected override Component? Render() =>
                    Button(OnClick: () =>
                    {
                        _n++;
                        StateHasChanged();
                    })["+"];
            }
            """;

        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new RedundantStateHasChangedAnalyzer(), new RedundantStateHasChangedCodeFixProvider(),
            "RASK026", source);

        Assert.DoesNotContain("StateHasChanged();", fixhed);
        Assert.Contains("_n++;", fixhed);
    }

    // ---- RASK027: both OnX and OnXAsync passed -> drop the async one ----

    [Fact]
    public async Task Rask027_RemovesTheAsyncArgument_AndKeepsTheSyncOne()
    {
        var source = """
            using System.Threading.Tasks;
            using Rask.Core;
            using static Rask.Core.Components.Generated;
            namespace Demo;
            public sealed class App : Component
            {
                protected override Component? Render() =>
                    Button(OnClick: () => {}, OnClickAsync: async () => await Task.Yield())["x"];
            }
            """;

        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new SyncAsyncHandlerAnalyzer(), new SyncAsyncHandlerCodeFixProvider(), "RASK027", source);

        Assert.DoesNotContain("OnClickAsync", fixhed);
        Assert.Contains("OnClick: () => {}", fixhed);
    }
}
