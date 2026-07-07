using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

public class RouteCollisionTests
{
    [Fact]
    public void TwoTopLevelPages_SameTemplate_ReportsRask031()
    {
        const string src = """
            using Rask.Core;
            using Rask.Core.Routing;
            namespace Demo;
            [Route("/products")] public sealed class ProductsA : Component { }
            [Route("/products")] public sealed class ProductsB : Component { }
            """;
        var run = GeneratorDriverFixture.RunRoutes(src);
        var d = run.Diagnostics.FirstOrDefault(x => x.Id == "RASK031");
        Assert.NotNull(d);
        Assert.Contains("/products", d!.GetMessage());
        // Names the page it collides with (the first by FQN, ProductsA).
        Assert.Contains("ProductsA", d.GetMessage());
        // Reported on exactly one page — the second (RunResult surfaces each generator diagnostic under
        // both the run and its result, so dedupe by source location before counting).
        var distinctLocations = run.Diagnostics
            .Where(x => x.Id == "RASK031")
            .Select(x => x.Location)
            .Distinct()
            .Count();
        Assert.Equal(1, distinctLocations);
    }

    [Theory]
    [InlineData("/Products", "/products")]      // literals match case-insensitively
    [InlineData("/products", "products/")]       // surrounding slashes are trimmed
    [InlineData("/item/{id:int}", "/item/{id:guid}")] // constraints aren't enforced at runtime
    [InlineData("/item/{id}", "/item/{slug}")]   // parameter names don't affect matching
    public void RuntimeEquivalentTemplates_Collide(string a, string b)
    {
        var src = $$"""
            using Rask.Core;
            using Rask.Core.Routing;
            namespace Demo;
            [Route("{{a}}")] public sealed class PageA : Component { }
            [Route("{{b}}")] public sealed class PageB : Component { }
            """;
        var run = GeneratorDriverFixture.RunRoutes(src);
        Assert.Contains(run.Diagnostics, x => x.Id == "RASK031");
    }

    [Fact]
    public void RequiredVsOptionalParam_DoNotCollide()
    {
        // A required and an optional parameter in the same position match different URL sets.
        const string src = """
            using Rask.Core;
            using Rask.Core.Routing;
            namespace Demo;
            [Route("/x/{a}")] public sealed class Req : Component { }
            [Route("/x/{a?}")] public sealed class Opt : Component { }
            """;
        var run = GeneratorDriverFixture.RunRoutes(src);
        Assert.DoesNotContain(run.Diagnostics, x => x.Id == "RASK031");
    }

    [Fact]
    public void Collision_IsWarning_NotError()
    {
        const string src = """
            using Rask.Core;
            using Rask.Core.Routing;
            namespace Demo;
            [Route("/dup")] public sealed class A : Component { }
            [Route("/dup")] public sealed class B : Component { }
            """;
        var run = GeneratorDriverFixture.RunRoutes(src);
        var d = run.Diagnostics.First(x => x.Id == "RASK031");
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
    }

    [Fact]
    public void DistinctTemplates_NoDiagnostic()
    {
        const string src = """
            using Rask.Core;
            using Rask.Core.Routing;
            namespace Demo;
            [Route("/a")] public sealed class PageA : Component { }
            [Route("/b")] public sealed class PageB : Component { }
            """;
        var run = GeneratorDriverFixture.RunRoutes(src);
        Assert.DoesNotContain(run.Diagnostics, x => x.Id == "RASK031");
    }

    [Fact]
    public void SameTemplate_UnderParentRoute_NotFlagged()
    {
        // A page with a [ParentRoute] composes its parent's path, so the local template alone isn't the
        // full URL — those are deliberately excluded to avoid false positives.
        const string src = """
            using Rask.Core;
            using Rask.Core.Routing;
            namespace Demo;
            [Route("/shop")] public sealed class Shop : Component { }
            [Route("/list")] public sealed class TopList : Component { }
            [Route("/list")][ParentRoute(typeof(Shop))] public sealed class NestedList : Component { }
            """;
        var run = GeneratorDriverFixture.RunRoutes(src);
        Assert.DoesNotContain(run.Diagnostics, x => x.Id == "RASK031");
    }

    [Fact]
    public void SamePage_DeclaredPartialTwice_NoFalsePositive()
    {
        const string src = """
            using Rask.Core;
            using Rask.Core.Routing;
            namespace Demo;
            [Route("/home")] public sealed partial class Home : Component { }
            public sealed partial class Home { }
            """;
        var run = GeneratorDriverFixture.RunRoutes(src);
        Assert.DoesNotContain(run.Diagnostics, x => x.Id == "RASK031");
    }
}
