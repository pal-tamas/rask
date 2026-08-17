using Rask.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

public class CodeFixProviderTests
{
    private static string App(string body) => $$"""
        using Rask.Core;
        using static Rask.Core.Components.Generated;
        namespace Demo;
        public sealed partial class App : Component
        {
            private static Component Wrap(Component c) => c;

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

    // A chain takes a `.Alt("")` STEP, not a named argument — appending `Alt: ""` into whichever step
    // happened to be nearest would not even compile.
    [Fact]
    public async Task Rask023_AppendsAltStep_ToAChain()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new ImgMissingAltAnalyzer(), new ImgMissingAltCodeFixProvider(), "RASK023",
            App("return Img.Src(\"/a.png\");"));
        Assert.Contains("Img.Src(\"/a.png\").Alt(\"\")", fixhed);
    }

    [Fact]
    public async Task Rask023_AppendsAltStep_ToABareEntry()
    {
        // A bare entry has no argument list at all, so only a step can be added.
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new ImgMissingAltAnalyzer(), new ImgMissingAltCodeFixProvider(), "RASK023",
            App("return Img;"));
        Assert.Contains("Img.Alt(\"\")", fixhed);
    }

    // The fix must act on the Img, never on whatever encloses it. Walking up to the nearest ancestor
    // invocation produced `Wrap(Img).Alt("")` — uncompilable, and the image still had no alt.
    [Fact]
    public async Task Rask023_AltLandsOnTheImg_NotOnAnEnclosingCall()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new ImgMissingAltAnalyzer(), new ImgMissingAltCodeFixProvider(), "RASK023",
            App("return Wrap(Img);"));

        Assert.Contains("Wrap(Img.Alt(\"\"))", fixhed);
        Assert.DoesNotContain("Wrap(Img).Alt", fixhed);
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

    // ---- RASK014: `new Widget()` -> the chain that builds it ----
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
        // A MARKUP HOST, because the bare entry the fix writes only binds inside one.
        partial class Caller : Component { protected override Component? Render() => null; void M() { {{body}} } }
        """;

    // The same construction outside a markup host: `Widget` there names the TYPE, so the rewrite would be
    // CS0119 — a worse error than the one it replaces. The fix is withheld rather than offered.
    private static string PlainCaller(string body) => $$"""
        using Rask.Core;
        namespace Demo;
        public sealed partial class Widget : Component
        {
            public Widget() { }
            public string? Id { get; set; }
            public override Component? Render() => this;
        }
        class NotAHost { void M() { {{body}} } }
        """;

    [Fact]
    public async Task Rask014_RewritesArgumentlessNew_ToTheBareEntry()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new ComponentConstructionAnalyzer(), new ComponentConstructionCodeFixProvider(), "RASK014",
            Caller("var x = new Widget();"));
        // The bare entry — which is what RASK014's own message tells the reader to write.
        Assert.Contains("var x = Widget;", fixhed);
        Assert.DoesNotContain("new Widget()", fixhed);
    }

    [Fact]
    public async Task Rask014_Withheld_OutsideAMarkupHost()
    {
        // Entries are protected static members on RaskMarkup, so `Widget` in a plain class names the TYPE
        // and the rewrite would be CS0119 — worse than the error it replaces. RASK014 still fires; only
        // the lightbulb stands down.
        var offered = await CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new ComponentConstructionAnalyzer(), new ComponentConstructionCodeFixProvider(), "RASK014",
            PlainCaller("var x = new Widget();"));

        Assert.False(offered);
    }

    [Fact]
    public async Task Rask014_DropsTheQualifier_BecauseTheEntryIsNotAType()
    {
        // `new Demo.Widget()` must become `Widget`, not `Demo.Widget` — the latter names a type where
        // a method has to go, and would not compile.
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new ComponentConstructionAnalyzer(), new ComponentConstructionCodeFixProvider(), "RASK014",
            Caller("var x = new Demo.Widget();"));
        Assert.Contains("var x = Widget;", fixhed);
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

    // On a chain the async handler is a STEP, so the fix splices it out rather than deleting an argument.
    [Fact]
    public async Task Rask027_RemovesTheAsyncStep_FromAChain()
    {
        var source = """
            using System.Threading.Tasks;
            using Rask.Core;
            namespace Demo;
            public sealed partial class App : Component
            {
                protected override Component? Render() =>
                    Button.OnClick(() => {}).OnClickAsync(async () => await Task.Yield())["x"];
            }
            """;
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new SyncAsyncHandlerAnalyzer(), new SyncAsyncHandlerCodeFixProvider(), "RASK027", source);

        Assert.DoesNotContain("OnClickAsync", fixhed);
        Assert.Contains("Button.OnClick(() => {})[\"x\"]", fixhed);
    }

    // The fix must never reach OUTSIDE the chain it was offered on. It used to walk up to the nearest
    // enclosing ArgumentSyntax, so a chain sitting in a named argument had that whole argument deleted —
    // the component silently disappeared and the remaining code still compiled.
    [Fact]
    public async Task Rask027_DoesNotDeleteTheEnclosingArgument()
    {
        var source = """
            using System.Threading.Tasks;
            using Rask.Core;
            namespace Demo;
            public sealed partial class App : Component
            {
                private static Component Wrap(Component Content, string Label) => Content;

                protected override Component? Render() =>
                    Wrap(
                        Content: Button.OnClick(() => {}).OnClickAsync(async () => await Task.Yield())["x"],
                        Label: "hi");
            }
            """;
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new SyncAsyncHandlerAnalyzer(), new SyncAsyncHandlerCodeFixProvider(), "RASK027", source);

        Assert.DoesNotContain("OnClickAsync", fixhed);
        Assert.Contains("Content:", fixhed);          // the argument survives
        Assert.Contains("Label: \"hi\"", fixhed);
        Assert.Contains("Button.OnClick(() => {})", fixhed);
    }

    // ---- CS0108: a member hides an inherited builder entry -> add `new` ----
    //
    // Every component type contributes an entry named after itself, so a member that shares a tag's or
    // a component's name hides one. The five shapes that produce it in this repo are a component
    // property, a private helper method named after a tag, a nested type, a field, and a `using` alias
    // (that last one is RASK037's — it is a hard CS1061, not a CS0108, so no fix can reach it).
    //
    // Each case hides a REAL entry — Footer, Section, Line and Table are `protected static` members the
    // generator emitted into Rask.Core.Component, read back out of the referenced assembly. Hiding a
    // hand-written stand-in instead would not only stop testing the real shape, it is ambiguous: the
    // stand-in hides the entry too, so the compilation carries two CS0108s and the fix lands on
    // whichever one GetDiagnostics happens to return first.
    private static string Hiding(string hider) => $$"""
        using Rask.Core;
        namespace Demo;
        public class Modal : Component
        {
            {{hider}}
        }
        """;

    [Fact]
    public async Task Cs0108_Property_GetsNewAfterAccessibility()
    {
        var fixhed = await CodeFixHarness.ApplyCompilerFixAsync(
            new HiddenBuilderEntryCodeFixProvider(), "CS0108",
            Hiding("public Component? Footer => null;"));
        Assert.Contains("public new Component? Footer => null;", fixhed);
    }

    [Fact]
    public async Task Cs0108_PrivateHelperMethod_GetsNew()
    {
        var fixhed = await CodeFixHarness.ApplyCompilerFixAsync(
            new HiddenBuilderEntryCodeFixProvider(), "CS0108",
            Hiding("private Component? Section() => null;"));
        Assert.Contains("private new Component? Section() => null;", fixhed);
    }

    [Fact]
    public async Task Cs0108_NestedType_GetsNewBeforeSealed()
    {
        // `new` sits after the accessibility and before `sealed`, which is where
        // csharp_preferred_modifier_order wants it — so the fix does not fight `dotnet format`.
        var fixhed = await CodeFixHarness.ApplyCompilerFixAsync(
            new HiddenBuilderEntryCodeFixProvider(), "CS0108",
            Hiding("public sealed record Line(int X);"));
        Assert.Contains("public new sealed record Line(int X);", fixhed);
    }

    [Fact]
    public async Task Cs0108_Field_GetsNewBeforeReadonly()
    {
        var fixhed = await CodeFixHarness.ApplyCompilerFixAsync(
            new HiddenBuilderEntryCodeFixProvider(), "CS0108",
            Hiding("private readonly string Table = \"\";"));
        Assert.Contains("private new readonly string Table = \"\";", fixhed);
    }

    [Fact]
    public async Task Cs0108_MemberWithNoModifiers_KeepsItsIndentation()
    {
        var fixhed = await CodeFixHarness.ApplyCompilerFixAsync(
            new HiddenBuilderEntryCodeFixProvider(), "CS0108",
            Hiding("string Table = \"\";"));
        Assert.Contains("    new string Table = \"\";", fixhed);
    }

    [Fact]
    public async Task Cs0108_OutsideAComponent_FixIsWithheld() =>
        // Hiding in a plain class hierarchy is the user's own design decision; Rask has no business
        // answering it, and a fix offered there would be applied by `dotnet format` unasked.
        Assert.False(await CodeFixHarness.IsCompilerFixOfferedAsync(
            new HiddenBuilderEntryCodeFixProvider(), "CS0108", """
                namespace Demo;
                public class Base { public int Count => 1; }
                public class Derived : Base { public int Count => 2; }
                """));
}
