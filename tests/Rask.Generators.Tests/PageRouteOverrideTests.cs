using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

/// <summary>
///     A page declares its route by overriding <c>Page.Route</c> with a compile-time constant, and gets a
///     generated <c>SomePage.Url(...)</c> / <c>SomePage.Go(...)</c> pair alongside the legacy
///     <c>Routes.SomePage(...)</c> factory.
/// </summary>
public class PageRouteOverrideTests
{
    [Fact]
    public void RouteOverride_RegistersTheTemplate()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class HomePage : Page
                  {
                      protected override string Route => "/";
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("public static global::Rask.Core.Routing.RouteUrl HomePage()", output);
        Assert.Contains("__path = \"/\"", output);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void RouteOverride_EmitsUrlAndGoExtensions()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  public sealed partial class ProductPage : Page
                  {
                      protected override string Route => "/products/{id:int}";
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
                  namespace Demo;
                  public sealed partial class HomePage : Page
                  {
                      protected override string Route => "/";
                      public override Component? Render() => this;
                  }
                  public sealed partial class AboutPage : Page
                  {
                      protected override string Route => "/about";
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("public static class __RaskNav_HomePage", output);
        Assert.Contains("public static class __RaskNav_AboutPage", output);
    }

    [Fact]
    public void ParentOverride_ComposesOntoTheParentTemplate()
    {
        var src = """
                  using System;
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class LayoutPage : Page
                  {
                      protected override string Route => "/app";
                      public override Component? Render() => this;
                  }
                  public sealed partial class SettingsPage : Page
                  {
                      protected override string Route => "settings";
                      protected override Type? Parent => typeof(LayoutPage);
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
        // GetConstantValue means a const (and constant concatenation) works, not just a literal.
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class DocsPage : Page
                  {
                      private const string Root = "/docs";
                      protected override string Route => Root + "/index";
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("\"/docs/index\"", output);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void NonConstantRoute_ReportsRask047()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class BadPage : Page
                  {
                      private static string Computed() => "/bad";
                      protected override string Route => Computed();
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK047" && d.Severity == DiagnosticSeverity.Error);
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
                  public sealed partial class SwapPage : Page
                  {
                      protected override string Route => "/swap/{replace}";
                      [RouteParam] public string Replace { get; set; } = "";
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("public static void Go(string Replace)", output);
        Assert.DoesNotContain("bool replace = false", output);
    }

    [Fact]
    public void GetterBodyReturn_IsAcceptedLikeAnExpressionBody()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed partial class BlockPage : Page
                  {
                      protected override string Route { get { return "/block"; } }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("\"/block\"", output);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
