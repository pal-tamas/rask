using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

/// <summary>
///     A routed component gets a generated <c>SomePage.Url(...)</c> / <c>SomePage.Go(...)</c> pair alongside
///     the <c>Routes.SomePage(...)</c> factory. Multi-route pages are covered in
///     <see cref="RoutesGeneratorTests" /> — these cover the shape of the helpers themselves.
/// </summary>
public class RouteNavigationHelperTests
{
    [Fact]
    public void Route_EmitsUrlAndGoExtensions()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/products/{id:int}")]
                  public sealed partial class ProductPage : Component
                  {
                      [RouteParam] public int Id { get; set; }
                      [QueryParam] public string? Sort { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        // Url mirrors the legacy factory's signature exactly and forwards to it, so the URL-building
        // logic keeps a single implementation.
        Assert.Contains("extension(global::Demo.ProductPage)", output);
        Assert.Contains("public static global::Rask.Core.Routing.RouteUrl Url(int Id, string? Sort = null)", output);
        Assert.Contains("=> Routes.ProductPage(Id, Sort);", output);

        // Go adds the history flag and routes through the ambient navigator.
        Assert.Contains("public static void Go(int Id, string? Sort = null, bool replace = false)", output);
        Assert.Contains("global::Rask.Core.Routing.Navigator.RequireCurrent().NavigateTo(Routes.ProductPage(Id, Sort), replace);",
            output);
    }

    [Fact]
    public void EachPage_GetsItsOwnExtensionContainer()
    {
        // Static extension members lower to plain statics on the containing class with no receiver
        // parameter, so two parameterless pages sharing one container would emit two identical `Url()`
        // signatures and fail with CS0111. One container per page is what keeps them apart.
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/")]
                  public sealed partial class HomePage : Component
                  {
                      public override Component? Render() => this;
                  }
                  [Route("/about")]
                  public sealed partial class AboutPage : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("public static class __RaskNav_HomePage", output);
        Assert.Contains("public static class __RaskNav_AboutPage", output);
    }

    [Fact]
    public void ParentRoute_ComposesOntoTheParentTemplate()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/app")]
                  public sealed partial class LayoutPage : Component
                  {
                      public override Component? Render() => this;
                  }
                  [Route("settings")]
                  [ParentRoute(typeof(LayoutPage))]
                  public sealed partial class SettingsPage : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("\"/app/settings\"", output);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ConstField_IsAcceptedAsTheTemplate()
    {
        // An attribute argument is constant by construction, and a const (or constant concatenation)
        // satisfies that just as a literal does.
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route(DocsPage.Root + "/index")]
                  public sealed partial class DocsPage : Component
                  {
                      public const string Root = "/docs";
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("\"/docs/index\"", output);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ParamNamedReplace_DropsGosHistoryFlag()
    {
        // The page's own parameter wins; Go loses the convenience flag rather than silently binding
        // the caller's `replace:` argument to the route parameter.
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/swap/{replace}")]
                  public sealed partial class SwapPage : Component
                  {
                      [RouteParam] public string Replace { get; set; } = "";
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("public static void Go(string Replace)", output);
        Assert.DoesNotContain("bool replace = false", output);
    }
}
