namespace Rask.Generators.Tests;

// M8: a property named with a C# keyword (verbatim identifier, e.g. `@event`) must emit a
// factory that COMPILES — ISymbol.Name strips the leading '@', so every emitted use of the name
// has to be re-escaped. M9: a partial routed page with attributes on more than one declaration
// must register exactly once, not once per attributed declaration.
public class KeywordIdentifierAndPartialRouteTests
{
    [Fact]
    public void A_keyword_named_property_gets_a_compilable_step()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Widget : Component
                  {
                      public string? @event { get; set; }   // optional keyword prop
                      public int @class { get; set; }        // required keyword prop (folded into propsChanged)
                      protected override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("RaskBuilderSetters.g.cs");

        // The emitted identifiers are '@'-escaped...
        Assert.Contains("__c.@event = value", output);
        Assert.Contains("__c.@class = value", output);
        // ...and the load-bearing guarantee: what the generator wrote compiles against the source.
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_keyword_named_route_and_query_parameter_gets_compilable_url_helpers()
    {
        var src = """
                  using System.Collections.Generic;
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/items/{Class}")]
                  public sealed class ItemsPage : Component
                  {
                      [RouteParam] public string Class { get; set; } = "";
                      [QueryParam] public string? @event { get; set; }
                      [QueryParam] public IReadOnlyList<string> @static { get; set; } = [];
                      protected override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Routes.g.cs");

        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("@event = null", output);
        Assert.Contains("var __element_static in @static", output);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_property_diagnostic_points_at_the_line_the_property_is_on()
    {
        // The location was rebuilt from a span with a 0,0 line span, so the IDE and the build log put it on line 1.
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [NotFound]
                  public sealed class FirstNotFound : Component
                  {
                      public override Component? Render() => this;
                  }

                  [NotFound]
                  public sealed class SecondNotFound : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);

        var dup = Assert.Single(run.Diagnostics, d => d.Id == "RASK012");
        Assert.Equal(9, dup.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void A_non_keyword_property_is_not_escaped()
    {
        // Escaping is a no-op for ordinary names — the common path is byte-for-byte as before.
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class Widget : Component
                  {
                      public string? Title { get; set; }
                      protected override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("RaskBuilderSetters.g.cs");

        Assert.Contains("__c.Title = value", output);
        Assert.DoesNotContain("@Title", output);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_partial_routed_page_with_attributes_on_multiple_declarations_registers_once()
    {
        var src = """
                  using System;
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/p")]
                  public sealed partial class PageA : Component
                  {
                      protected override Component? Render() => this;
                  }
                  [Obsolete]
                  public sealed partial class PageA
                  {
                      public int X { get; set; }
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var reg = run.GeneratedSource("__RaskRoutesRegistry");

        // Was: two RouteRegistration entries + two [DynamicDependency] (one per attributed part).
        Assert.Equal(1, CountOccurrences(reg, "new(typeof(global::Demo.PageA), \"/p\", null)"));
        Assert.Equal(1, CountOccurrences(reg, "DynamicDependency"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }

        return count;
    }
}
