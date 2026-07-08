using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

public class RoutesGeneratorTests
{
    [Fact]
    public void RootTemplate_NoParams_EmitsZeroArgFactory()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/")]
                  public sealed class HomePage : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("public static partial class Routes", output);
        Assert.Contains("public static global::Rask.Core.Routing.RouteUrl HomePage()", output);
        Assert.Contains("typeof(global::Demo.HomePage)", output);
        Assert.Contains("__path = \"/\"", output);
    }

    [Fact]
    public void TypedIntPathParam_EmitsTypedParameter()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/users/{id:int}")]
                  public sealed class UserPage : Component
                  {
                      [RouteParam] public int Id { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("UserPage(int Id)", output);
        Assert.Contains("global::Rask.Core.Routing.RouteValueFormatter.Format(Id)", output);
        Assert.DoesNotContain("Id.ToString(", output);
    }

    [Fact]
    public void OptionalStringPathParam_EmitsNullableWithGuard()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/counter/{name?}")]
                  public sealed class CounterPage : Component
                  {
                      [RouteParam] public string? Name { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("CounterPage(string? Name = null)", output);
        Assert.Contains("Name is null ? \"\" : \"/\" + global::Rask.Core.Routing.RouteValueFormatter.Format(Name)",
            output);
    }

    [Fact]
    public void TypeMismatch_RaisesRask005()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/users/{id:int}")]
                  public sealed class UserPage : Component
                  {
                      [RouteParam] public string Id { get; set; } = "";
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        Assert.Contains(run.Diagnostics, d => d.Id == "RASK005");
    }

    [Fact]
    public void MissingProperty_RaisesRask004()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/users/{id:int}")]
                  public sealed class UserPage : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        Assert.Contains(run.Diagnostics, d => d.Id == "RASK004");
    }

    [Fact]
    public void Rask004_Message_StatesHowToFix()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/users/{id:int}")]
                  public sealed class UserPage : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var message = run.Diagnostics.First(d => d.Id == "RASK004").GetMessage();

        // The message must carry the remedy, not just the problem (D6 actionable-clause audit).
        Assert.Contains(" — ", message);
        Assert.Contains("add a public settable property", message);
    }

    [Fact]
    public void QueryParam_EmitsOptionalParameter()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/counter/{name?}")]
                  public sealed class CounterPage : Component
                  {
                      [RouteParam] public string? Name { get; set; }
                      [QueryParam] public string? Greeting { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("CounterPage(string? Name = null, string? Greeting = null)", output);
        Assert.Contains("Greeting=", output);
        Assert.Contains("global::Rask.Core.Routing.RouteValueFormatter.Format(Greeting)", output);
    }

    [Fact]
    public void QueryParam_ExplicitName_OverridesPropertyName()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/")]
                  public sealed class HomePage : Component
                  {
                      [QueryParam("q")] public string? Search { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("\"q=\"", output);
        Assert.DoesNotContain("\"Search=\"", output);
    }

    [Fact]
    public void QueryParam_NameWithSpecialChars_IsUrlEncodedInGeneratedKey()
    {
        // An explicit query-param name with characters that are special in a query string must be
        // URL-encoded in the emitted key, else the generated URL is malformed.
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/")]
                  public sealed class HomePage : Component
                  {
                      [QueryParam("a b&c")] public string? Search { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("\"a%20b%26c=\"", output);
        Assert.DoesNotContain("\"a b&c=\"", output);
    }

    [Fact]
    public void ParentRoute_PrefixesTemplateWithParentTemplate()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/dashboard")]
                  public sealed class DashboardPage : Component
                  {
                      public override Component? Render() => this;
                  }
                  [Route("overview")]
                  [ParentRoute(typeof(DashboardPage))]
                  public sealed class DashOverview : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("DashOverview()", output);
        Assert.Contains("\"/dashboard/overview\"", output);
    }

    [Fact]
    public void ParentRouteCycle_RaisesRask007()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/a")]
                  [ParentRoute(typeof(B))]
                  public sealed class A : Component { public override Component? Render() => this; }

                  [Route("/b")]
                  [ParentRoute(typeof(A))]
                  public sealed class B : Component { public override Component? Render() => this; }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        Assert.Contains(run.Diagnostics, d => d.Id == "RASK007");
    }

    [Fact]
    public void GuidConstraint_EmitsGuidParameterType()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/orders/{id:guid}")]
                  public sealed class OrderPage : Component
                  {
                      [RouteParam] public System.Guid Id { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("OrderPage(global::System.Guid Id)", output);
    }

    [Fact]
    public void UnconstrainedSegment_TreatedAsString()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/blog/{slug}")]
                  public sealed class BlogPostPage : Component
                  {
                      [RouteParam] public string Slug { get; set; } = "";
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("BlogPostPage(string Slug)", output);
        Assert.Contains("global::Rask.Core.Routing.RouteValueFormatter.Format(Slug)", output);
    }

    [Fact]
    public void NoRouteAttribute_EmitsNothing()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Plain : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var hasRoutesFile = run.RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Any(s => s.HintName.Contains("Routes.g.cs"));
        Assert.False(hasRoutesFile);
    }

    [Fact]
    public void RegistryInitializer_EmitsModuleInitializerAndRegistrations()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/")] public sealed class HomePage : Component { public override Component? Render() => this; }
                  [Route("/dashboard")] public sealed class DashPage : Component { public override Component? Render() => this; }
                  [Route("overview")] [ParentRoute(typeof(DashPage))]
                  public sealed class DashOverview : Component { public override Component? Render() => this; }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("__RaskRoutesRegistry.g.cs");

        Assert.Contains("internal static class __RaskRoutesRegistry", output);
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", output);
        Assert.Contains("global::Rask.Core.Routing.RouteRegistry.Add", output);
        Assert.Contains("new(typeof(global::Demo.HomePage), \"/\", null)", output);
        Assert.Contains("new(typeof(global::Demo.DashPage), \"/dashboard\", null)", output);
        Assert.Contains("new(typeof(global::Demo.DashOverview), \"overview\", typeof(global::Demo.DashPage))", output);
    }

    [Fact]
    public void RegistryInitializer_RegistersCustomParsableParamType_ForAot()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  using System;
                  namespace Demo;
                  public readonly record struct Sku(string Code) : IParsable<Sku>
                  {
                      public static Sku Parse(string s, IFormatProvider? p) => new(s);
                      public static bool TryParse(string? s, IFormatProvider? p, out Sku r) { r = new(s ?? ""); return s is not null; }
                  }
                  [Route("/products/{sku}")]
                  public sealed class ProductPage : Component
                  {
                      [RouteParam] public Sku Sku { get; set; }
                      [QueryParam] public int Page { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("__RaskRoutesRegistry.g.cs");

        // Custom IParsable type is registered so a full-AOT build can bind it without MakeGenericMethod.
        Assert.Contains("global::Rask.Core.Forms.RaskBinding.RegisterParsable<global::Demo.Sku>();", output);
        // BCL primitives are seeded by the framework — never emitted.
        Assert.DoesNotContain("RegisterParsable<int>", output);
        Assert.DoesNotContain("RegisterParsable<global::System.Int32>", output);
    }

    [Fact]
    public void RegistryInitializer_RegistersNonPrimitiveBclParsableParamType()
    {
        // Registration keys off SpecialType (not the namespace), so any non-primitive IParsable type
        // is registered even when it lives under System.* — e.g. System.Net.IPAddress, which the
        // framework does not seed. This keeps a future unseeded BCL parsable from silently failing to
        // bind under full AOT, without the generator having to mirror the registry's seed list.
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  using System.Net;
                  namespace Demo;
                  [Route("/host/{ip}")]
                  public sealed class HostPage : Component
                  {
                      [RouteParam] public IPAddress Ip { get; set; } = null!;
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("__RaskRoutesRegistry.g.cs");

        Assert.Contains("global::Rask.Core.Forms.RaskBinding.RegisterParsable<global::System.Net.IPAddress>();", output);
    }

    [Fact]
    public void RegistryInitializer_NoCustomParsableParams_EmitsNoRegistrations()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/users/{id:int}")]
                  public sealed class UserPage : Component
                  {
                      [RouteParam] public int Id { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("__RaskRoutesRegistry.g.cs");

        Assert.DoesNotContain("RegisterParsable", output);
    }

    [Fact]
    public void OrphanRouteParam_RaisesRask008()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/users")]
                  public sealed class UserPage : Component
                  {
                      [RouteParam] public string? Stray { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        Assert.Contains(run.Diagnostics, d => d.Id == "RASK008");
    }

    [Fact]
    public void RouteParamWithExplicitName_MatchesSegmentByOverride()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/users/{id:int}")]
                  public sealed class UserPage : Component
                  {
                      [RouteParam("id")] public int UserId { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("UserPage(int UserId)", output);
        Assert.DoesNotContain("RASK004", output);
    }

    [Fact]
    public void PathSegmentWithoutRouteParamProperty_RaisesRask004()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/users/{id:int}")]
                  public sealed class UserPage : Component
                  {
                      public int Id { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        Assert.Contains(run.Diagnostics, d => d.Id == "RASK004");
    }

    [Fact]
    public void RouteParamOnNonRouteClass_RaisesRask009()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  public sealed class Stray : Component
                  {
                      [RouteParam] public int Id { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var diag = run.Diagnostics.FirstOrDefault(d => d.Id == "RASK009");
        Assert.NotNull(diag);
        Assert.Contains("not marked [Route]", diag!.GetMessage());
    }

    [Fact]
    public void QueryParamOnNonRouteClass_RaisesRask010()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  public sealed class Stray : Component
                  {
                      [QueryParam] public string? Q { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var diag = run.Diagnostics.FirstOrDefault(d => d.Id == "RASK010");
        Assert.NotNull(diag);
        Assert.Contains("not marked [Route]", diag!.GetMessage());
    }

    [Fact]
    public void RouteParamOnNonComponentClass_RaisesRask009()
    {
        var src = """
                  using Rask.Core.Routing;
                  namespace Demo;
                  public sealed class Poco
                  {
                      [RouteParam] public int Id { get; set; }
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var diag = run.Diagnostics.FirstOrDefault(d => d.Id == "RASK009");
        Assert.NotNull(diag);
        Assert.Contains("does not inherit from Component", diag!.GetMessage());
    }

    [Fact]
    public void QueryParamOnAbstractComponent_RaisesRask010()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/base")]
                  public abstract class BasePage : Component
                  {
                      [QueryParam] public string? Q { get; set; }
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var diag = run.Diagnostics.FirstOrDefault(d => d.Id == "RASK010");
        Assert.NotNull(diag);
        Assert.Contains("class is abstract", diag!.GetMessage());
    }

    [Fact]
    public void ValidRouteClass_NoOrphanDiagnostic()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/users/{id:int}")]
                  public sealed class UserPage : Component
                  {
                      [RouteParam] public int Id { get; set; }
                      [QueryParam] public string? Q { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK009" || d.Id == "RASK010");
    }

    [Fact]
    public void NonParsableType_RaisesRask011()
    {
        var src = """
                  using System.Collections.Generic;
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/x")]
                  public sealed class XPage : Component
                  {
                      [QueryParam] public List<int>? Bad { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        Assert.Contains(run.Diagnostics, d => d.Id == "RASK011");
    }

    [Fact]
    public void CustomIParsable_PathParam_EmitsPropertyType()
    {
        var src = """
                  using System;
                  using System.Globalization;
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  public readonly record struct CustomerId(int Value) : IParsable<CustomerId>
                  {
                      public static CustomerId Parse(string s, IFormatProvider? p) => new(int.Parse(s, p));
                      public static bool TryParse(string? s, IFormatProvider? p, out CustomerId result)
                      {
                          if (int.TryParse(s, NumberStyles.Integer, p, out var v)) { result = new(v); return true; }
                          result = default; return false;
                      }
                  }
                  [Route("/customers/{id}")]
                  public sealed class CustomerPage : Component
                  {
                      [RouteParam] public CustomerId Id { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("CustomerPage(global::Demo.CustomerId Id)", output);
        Assert.Contains("global::Rask.Core.Routing.RouteValueFormatter.Format(Id)", output);
    }

    [Fact]
    public void CustomIParsable_QueryParam_EmitsPropertyType()
    {
        var src = """
                  using System;
                  using System.Globalization;
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  public readonly record struct PageNumber(int N) : IParsable<PageNumber>
                  {
                      public static PageNumber Parse(string s, IFormatProvider? p) => new(int.Parse(s, p));
                      public static bool TryParse(string? s, IFormatProvider? p, out PageNumber result)
                      {
                          if (int.TryParse(s, NumberStyles.Integer, p, out var v)) { result = new(v); return true; }
                          result = default; return false;
                      }
                  }
                  [Route("/list")]
                  public sealed class ListPage : Component
                  {
                      [QueryParam] public PageNumber? Page { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        Assert.Contains("ListPage(global::Demo.PageNumber? Page = null)", output);
        Assert.Contains("global::Rask.Core.Routing.RouteValueFormatter.Format(Page)", output);
    }

    [Fact]
    public void MultipleRouteAttributes_RegisterEveryTemplateUnderSameType()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/todos")]
                  [Route("/todos/new")]
                  [Route("/todos/{id:guid}/edit")]
                  public sealed class TodosPage : Component
                  {
                      [RouteParam] public System.Guid? Id { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var registry = run.GeneratedSource("__RaskRoutesRegistry.g.cs");
        Assert.Contains("new(typeof(global::Demo.TodosPage), \"/todos\", null)", registry);
        Assert.Contains("new(typeof(global::Demo.TodosPage), \"/todos/new\", null)", registry);
        Assert.Contains("new(typeof(global::Demo.TodosPage), \"/todos/{id:guid}/edit\", null)", registry);

        // DynamicDependency is per-type, not per-template — exactly one entry per type.
        var dynDepCount = Regex
            .Matches(registry, "typeof\\(global::Demo\\.TodosPage\\)\\)\\]").Count;
        Assert.Equal(1, dynDepCount);
    }

    [Fact]
    public void MultipleRouteAttributes_UrlFormatterUsesFirstTemplateOnly()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/todos")]
                  [Route("/todos/new")]
                  [Route("/todos/{id:guid}/edit")]
                  public sealed class TodosPage : Component
                  {
                      [RouteParam] public System.Guid? Id { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Demo.Routes.g.cs");

        // Canonical formatter derives from the first template (/todos), which takes no params.
        Assert.Contains("public static global::Rask.Core.Routing.RouteUrl TodosPage()", output);
        // No extra overloads/suffixed formatters for the other templates.
        Assert.DoesNotContain("TodosPage_", output);
        Assert.DoesNotContain("TodosPage(global::System.Guid", output);
    }

    [Fact]
    public void IdenticalSource_ProducesByteIdenticalOutput()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/users/{id:int}")]
                  public sealed class UserPage : Component
                  {
                      [RouteParam] public int Id { get; set; }
                      public override Component? Render() => this;
                  }
                  """;

        var a = GeneratorDriverFixture.RunRoutes(src).GeneratedSource("Demo.Routes.g.cs");
        var b = GeneratorDriverFixture.RunRoutes(src).GeneratedSource("Demo.Routes.g.cs");
        Assert.Equal(a, b);
    }
}
