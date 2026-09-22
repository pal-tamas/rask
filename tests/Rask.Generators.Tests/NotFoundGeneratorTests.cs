namespace Rask.Generators.Tests;

public class NotFoundGeneratorTests
{
    [Fact]
    public void A_NotFound_page_emits_a_catch_all_registration_without_a_Routes_factory()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [NotFound]
                  public sealed class MyNotFound : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var registry = run.GeneratedSource("__RaskRoutesRegistry.g.cs");

        Assert.Contains("new(typeof(global::Demo.MyNotFound), \"{**__rask_notfound}\", null)", registry);

        var hasRoutesFile = run.RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Any(s => s.HintName.Contains("Routes.g.cs") && !s.HintName.StartsWith("__"));
        Assert.False(hasRoutesFile, "[NotFound] components must not produce a Routes.X() factory");
    }

    [Fact]
    public void A_NotFound_page_with_a_ParentRoute_flows_the_parent_into_its_registration()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/")]
                  public sealed class Layout : Component { public override Component? Render() => this; }

                  [NotFound]
                  [ParentRoute(typeof(Layout))]
                  public sealed class MyNotFound : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var registry = run.GeneratedSource("__RaskRoutesRegistry.g.cs");

        Assert.Contains(
            "new(typeof(global::Demo.MyNotFound), \"{**__rask_notfound}\", typeof(global::Demo.Layout))",
            registry);
    }

    [Fact]
    public void A_NotFound_page_with_a_Route_attribute_raises_RASK013()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/foo")]
                  [NotFound]
                  public sealed class Bad : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK013");
    }

    [Fact]
    public void A_duplicate_NotFound_page_raises_RASK012_naming_it()
    {
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

        var dup = run.Diagnostics.FirstOrDefault(d => d.Id == "RASK012");
        Assert.NotNull(dup);
        Assert.Contains("SecondNotFound", dup!.GetMessage());
    }

    [Fact]
    public void Of_duplicate_NotFound_pages_only_the_first_survives_in_the_registry()
    {
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
        var registry = run.GeneratedSource("__RaskRoutesRegistry.g.cs");

        Assert.Contains("FirstNotFound", registry);
        Assert.DoesNotContain("SecondNotFound", registry);
    }

    [Fact]
    public void A_NotFound_page_with_no_other_routes_skips_the_Routes_partial()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [NotFound]
                  public sealed class MyNotFound : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);

        var hasNamespaceRoutesFile = run.RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Any(s => s.HintName == "Demo.Routes.g.cs");
        Assert.False(hasNamespaceRoutesFile);
    }

    [Fact]
    public void Beside_a_NotFound_page_only_the_routed_page_gets_a_factory()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/")]
                  public sealed partial class HomePage : Component { public override Component? Render() => this; }

                  [NotFound]
                  public sealed class MyNotFound : Component { public override Component? Render() => this; }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var routes = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("public static global::Rask.Core.Routing.RouteUrl HomePage()", routes);
        Assert.DoesNotContain("MyNotFound(", routes);
    }
}
