using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

public class RoutesGeneratorTests
{
    [Fact]
    public void A_root_template_with_no_params_emits_a_zero_arg_factory()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/")]
                  public sealed partial class HomePage : Component
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
    public void A_typed_int_path_param_emits_a_typed_parameter()
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
    public void An_optional_string_path_param_emits_a_nullable_with_a_guard()
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
    public void A_type_mismatch_raises_RASK005()
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
    public void A_missing_property_raises_RASK004()
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
    public void The_RASK004_message_states_how_to_fix_it()
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
    public void A_QueryParam_emits_an_optional_parameter()
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
    public void An_explicit_QueryParam_name_overrides_the_property_name()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/")]
                  public sealed partial class HomePage : Component
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
    public void A_QueryParam_name_with_special_chars_is_url_encoded_in_the_generated_key()
    {
        // An explicit query-param name with characters that are special in a query string must be
        // URL-encoded in the emitted key, else the generated URL is malformed.
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/")]
                  public sealed partial class HomePage : Component
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
    public void A_ParentRoute_prefixes_the_template_with_the_parent_template()
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
    public void A_cycle_of_parent_routes_raises_RASK007()
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
    public void A_guid_constraint_emits_a_Guid_parameter_type()
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
    public void An_unconstrained_segment_is_treated_as_a_string()
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
    public void A_class_with_no_Route_attribute_emits_nothing()
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
    public void The_registry_initializer_emits_a_module_initializer_and_the_registrations()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/")] public sealed partial class HomePage : Component { public override Component? Render() => this; }
                  [Route("/dashboard")] public sealed class DashPage : Component { public override Component? Render() => this; }
                  [Route("overview")] [ParentRoute(typeof(DashPage))]
                  public sealed class DashOverview : Component { public override Component? Render() => this; }
                  """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("__RaskRoutesRegistry.g.cs");

        Assert.Contains("internal static class __RaskRoutesRegistry", output);
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", output);
        // Replace, not Add: the registrations moved into a re-invocable RefreshAll() so routes can
        // hot-reload, and a refresh has to swap this assembly's set rather than append to it.
        // RoutesRegistryRefreshTests covers that split in full.
        Assert.Contains("global::Rask.Core.Routing.RouteRegistry.Replace(typeof(__RaskRoutesRegistry)", output);
        Assert.Contains("new(typeof(global::Demo.HomePage), \"/\", null)", output);
        Assert.Contains("new(typeof(global::Demo.DashPage), \"/dashboard\", null)", output);
        Assert.Contains("new(typeof(global::Demo.DashOverview), \"overview\", typeof(global::Demo.DashPage))", output);
    }

    [Fact]
    public void The_registry_initializer_registers_a_custom_parsable_param_type_for_AOT()
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
    public void The_registry_initializer_registers_a_non_primitive_BCL_parsable_param_type()
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
    public void The_registry_initializer_emits_no_registrations_without_custom_parsable_params()
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
    public void A_RouteParam_with_no_segment_to_fill_raises_RASK008()
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
    public void A_RouteParam_with_an_explicit_name_matches_the_segment_by_that_override()
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
    public void A_path_segment_without_a_RouteParam_property_raises_RASK004()
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
    public void A_RouteParam_on_a_class_without_a_route_raises_RASK009()
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
        Assert.Contains("has no [Route]", diag!.GetMessage());
    }

    [Fact]
    public void A_QueryParam_on_a_class_without_a_route_raises_RASK010()
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
        Assert.Contains("has no [Route]", diag!.GetMessage());
    }

    [Fact]
    public void A_RouteParam_on_a_class_that_is_not_a_component_raises_RASK009()
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
    public void A_QueryParam_on_an_abstract_component_raises_RASK010()
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
    public void A_valid_route_class_raises_no_orphan_diagnostic()
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
    public void A_non_parsable_type_raises_RASK011()
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
    public void A_custom_IParsable_path_param_emits_its_property_type()
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
    public void A_custom_IParsable_query_param_emits_its_property_type()
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
    public void Several_Route_attributes_register_every_template_under_the_same_type()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/todos")]
                  [Route("/todos/new")]
                  [Route("/todos/{id:guid}/edit")]
                  public sealed partial class TodosPage : Component
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
    public void With_several_Route_attributes_the_url_formatter_uses_only_the_first_template()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;
                  [Route("/todos")]
                  [Route("/todos/new")]
                  [Route("/todos/{id:guid}/edit")]
                  public sealed partial class TodosPage : Component
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
    public void Identical_source_produces_byte_identical_output()
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
