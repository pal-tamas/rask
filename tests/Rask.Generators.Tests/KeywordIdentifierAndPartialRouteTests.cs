namespace Rask.Generators.Tests;

// M8: a property named with a C# keyword (verbatim identifier, e.g. `@event`) must emit a
// factory that COMPILES — ISymbol.Name strips the leading '@', so every emitted use of the name
// has to be re-escaped. M9: a partial routed page with attributes on more than one declaration
// must register exactly once, not once per attributed declaration.
public class KeywordIdentifierAndPartialRouteTests
{
    [Fact]
    public void KeywordNamedProperty_GetsACompilableStep()
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
    public void NonKeywordProperty_IsNotEscaped()
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
    public void PartialRoutedPage_AttributesOnMultipleDeclarations_RegistersOnce()
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
