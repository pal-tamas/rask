using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

public class CodeFixProviderTests
{
    private static string App(string body) => $$"""
        using Rask.Core;
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

    // A chain takes a `.Alt("")` STEP, not a named argument.
    [Fact]
    public async Task The_RASK023_fix_appends_an_Alt_step_to_a_chain()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new ImgMissingAltAnalyzer(), new ImgMissingAltCodeFixProvider(), "RASK023",
            App("return Img.Src(\"/a.png\");"));

        Assert.Contains("Img.Src(\"/a.png\").Alt(\"\")", fixhed);
    }

    [Fact]
    public async Task The_RASK023_fix_appends_an_Alt_step_to_a_bare_entry()
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
    public async Task The_RASK023_fix_puts_Alt_on_the_Img_not_on_an_enclosing_call()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new ImgMissingAltAnalyzer(), new ImgMissingAltCodeFixProvider(), "RASK023",
            App("return Wrap(Img);"));

        Assert.Contains("Wrap(Img.Alt(\"\"))", fixhed);
        Assert.DoesNotContain("Wrap(Img).Alt", fixhed);
    }

    // ---- RASK001: property becomes a required factory param -> add `required` ----

    [Fact]
    public async Task The_RASK001_fix_adds_the_required_modifier_after_the_accessibility()
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
    public async Task The_RASK001_fix_is_offered_with_a_DI_constructor_and_no_parameterless_one()
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
    public async Task The_RASK001_fix_is_offered_when_the_DI_constructor_has_a_parameterless_sibling()
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
    public async Task The_RASK014_fix_rewrites_an_argumentless_new_to_the_bare_entry()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new ComponentConstructionAnalyzer(), new ComponentConstructionCodeFixProvider(), "RASK014",
            Caller("var x = new Widget();"));

        // The bare entry — which is what RASK014's own message tells the reader to write.
        Assert.Contains("var x = Widget;", fixhed);
        Assert.DoesNotContain("new Widget()", fixhed);
    }

    [Fact]
    public async Task The_RASK014_fix_is_withheld_outside_a_markup_host()
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
    public async Task The_RASK014_fix_drops_the_qualifier_because_the_entry_is_not_a_type()
    {
        // `new Demo.Widget()` must become `Widget`, not `Demo.Widget` — the latter names a type where
        // a method has to go, and would not compile.
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new ComponentConstructionAnalyzer(), new ComponentConstructionCodeFixProvider(), "RASK014",
            Caller("var x = new Demo.Widget();"));

        Assert.Contains("var x = Widget;", fixhed);
    }

    [Fact]
    public async Task The_RASK014_fix_is_withheld_when_the_construction_has_arguments()
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
    public async Task The_RASK014_fix_is_withheld_when_an_object_initializer_is_present()
    {
        // An object initializer is only legal after `new`, so it cannot ride along onto a factory call.
        var offered = await CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new ComponentConstructionAnalyzer(), new ComponentConstructionCodeFixProvider(), "RASK014",
            Caller("var x = new Widget { Id = \"a\" };"));

        Assert.False(offered);
    }

    // ---- RASK026: a StateHasChanged() the framework already does -> delete the statement ----

    [Fact]
    public async Task The_RASK026_fix_deletes_the_redundant_call()
    {
        var source = """
            using Rask.Core;
            namespace Demo;
            public sealed partial class App : Component
            {
                private int _n;
                protected override Component? Render() =>
                    Button.OnClick(() =>
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

    // ---- RASK071: ASP.NET's [Route] -> Rask's own ----

    // The common shape: the file imports MVC (that is how the wrong attribute got typed) and does not
    // import Rask.Core.Routing. A bare Route here would bind straight back to MVC's attribute, so the
    // fix has to leave the name qualified.
    [Fact]
    public async Task The_RASK071_fix_writes_the_qualified_name_with_MVC_imported()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new AspNetRouteAttributeAnalyzer(), new AspNetRouteCodeFixProvider(), "RASK071",
            Page("using Microsoft.AspNetCore.Mvc;", "[Route(\"/orders\")]"));

        Assert.Contains("Rask.Core.Routing.Route(\"/orders\")", fixhed);
        // The fix is worthless if it swaps one silent non-registration for another.
        Assert.DoesNotContain("[Route(\"/orders\")]", fixhed);
    }

    // With Rask's namespace already imported and nothing to collide with, the simplifier reduces the
    // qualified name to the spelling a person would have written.
    [Fact]
    public async Task The_RASK071_fix_reduces_to_the_short_name_with_Rask_routing_imported()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new AspNetRouteAttributeAnalyzer(), new AspNetRouteCodeFixProvider(), "RASK071",
            Page("using Rask.Core.Routing;", "[Microsoft.AspNetCore.Mvc.Route(\"/orders\")]"));

        Assert.Contains("[Route(\"/orders\")]", fixhed);
        Assert.DoesNotContain("Microsoft.AspNetCore.Mvc", fixhed);
    }

    // Both namespaces in scope is the case that punishes a naive fix: a bare Route is ambiguous
    // (CS0104), so the qualified form has to survive rather than be reduced away.
    [Fact]
    public async Task The_RASK071_fix_stays_qualified_with_both_namespaces_imported()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new AspNetRouteAttributeAnalyzer(), new AspNetRouteCodeFixProvider(), "RASK071",
            Page("using Microsoft.AspNetCore.Mvc;\nusing Rask.Core.Routing;",
                "[Microsoft.AspNetCore.Mvc.Route(\"/orders\")]"));

        Assert.Contains("Rask.Core.Routing.Route(\"/orders\")", fixhed);
    }

    [Fact]
    public async Task The_RASK071_fix_fixes_Blazors_attribute_too()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new AspNetRouteAttributeAnalyzer(), new AspNetRouteCodeFixProvider(), "RASK071",
            Page("using Rask.Core.Routing;", "[Microsoft.AspNetCore.Components.Route(\"/orders\")]"));

        Assert.Contains("[Route(\"/orders\")]", fixhed);
        Assert.DoesNotContain("Microsoft.AspNetCore.Components", fixhed);
    }

    // Rewriting only the NAME rather than rebuilding the attribute is what keeps the template intact,
    // constraint and all — a rebuilt attribute is where a route template quietly loses its ":int".
    [Fact]
    public async Task The_RASK071_fix_keeps_the_template_exactly()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new AspNetRouteAttributeAnalyzer(), new AspNetRouteCodeFixProvider(), "RASK071",
            Page("using Rask.Core.Routing;",
                "[Microsoft.AspNetCore.Mvc.Route(\"/orders/{id:int}\")]"));

        Assert.Contains("\"/orders/{id:int}\"", fixhed);
    }


    // MVC's attribute carries settable Name and Order; Rask's has neither. The arguments are carried
    // over verbatim, so offering the fix here would answer RASK071 with CS0117 on a property that does
    // not exist — a worse diagnostic than the one being fixed, and on a line the developer did not touch.
    [Fact]
    public async Task The_RASK071_fix_is_withheld_with_MVC_only_properties() =>
        Assert.False(await CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new AspNetRouteAttributeAnalyzer(), new AspNetRouteCodeFixProvider(), "RASK071",
            Page("using Rask.Core.Routing;",
                "[Microsoft.AspNetCore.Mvc.Route(\"/orders\", Name = \"orders\", Order = 2)]")));

    // An alias that bakes its own template in takes no arguments at all, so the rewritten attribute
    // would be missing the template Rask's constructor requires — CS7036.
    [Fact]
    public async Task The_RASK071_fix_is_withheld_for_an_alias_with_no_arguments() =>
        Assert.False(await CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new AspNetRouteAttributeAnalyzer(), new AspNetRouteCodeFixProvider(), "RASK071", """
                using Rask.Core;
                namespace Demo;

                public sealed class ApiRouteAttribute() : Microsoft.AspNetCore.Mvc.RouteAttribute("/orders");

                [ApiRoute]
                public sealed partial class Orders : Component
                {
                    protected override Component? Render() => null;
                }
                """));

    // The parameter-naming form is not the property form: Rask's constructor parameter is also called
    // `template`, so this one carries over intact and the fix stays on offer.
    [Fact]
    public async Task The_RASK071_fix_still_applies_with_the_template_named_as_a_parameter()
    {
        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new AspNetRouteAttributeAnalyzer(), new AspNetRouteCodeFixProvider(), "RASK071",
            Page("using Rask.Core.Routing;", "[Microsoft.AspNetCore.Mvc.Route(template: \"/orders\")]"));

        Assert.Contains("[Route(template: \"/orders\")]", fixhed);
    }

    private static string Page(string usings, string attribute) => $$"""
        using Rask.Core;
        {{usings}}
        namespace Demo;

        {{attribute}}
        public sealed partial class Orders : Component
        {
            protected override Component? Render() => null;
        }
        """;

    // The trap this fix exists to avoid, asserted on the COMPILER rather than on the text: a server file
    // holding both a genuine MVC controller and a Rask page. Reaching for the tidier fix here — insert
    // `using Rask.Core.Routing;` and leave a bare Route — would make the CONTROLLER's attribute ambiguous
    // and trade RASK071 for CS0104 somewhere the developer never looked. Asserting "no using was added"
    // would pin today's mechanism; compiling the result pins the property that actually matters.
    [Fact]
    public async Task The_RASK071_fix_does_not_break_an_MVC_controller_sharing_the_file()
    {
        var source = """
                     using Rask.Core;
                     using Microsoft.AspNetCore.Mvc;
                     namespace Demo;

                     [Route("/api/orders")]
                     public sealed class OrdersController : ControllerBase
                     {
                         public string Get() => "ok";
                     }

                     [Route("/orders")]
                     public sealed partial class Orders : Component
                     {
                         protected override Component? Render() => null;
                     }
                     """;

        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new AspNetRouteAttributeAnalyzer(), new AspNetRouteCodeFixProvider(), "RASK071", source);

        Assert.Contains("Rask.Core.Routing.Route(\"/orders\")", fixhed);
        // The controller is untouched and still binds to MVC's attribute.
        Assert.Contains("[Route(\"/api/orders\")]", fixhed);

        var errors = CSharpCompilation.Create(
                "Fixed",
                [CSharpSyntaxTree.ParseText(fixhed, new CSharpParseOptions(LanguageVersion.Latest))],
                GeneratorDriverFixture.BuildReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable))
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            "the fixed file must still compile:\n  " + string.Join("\n  ", errors.Select(d => d.ToString())));
    }
}
