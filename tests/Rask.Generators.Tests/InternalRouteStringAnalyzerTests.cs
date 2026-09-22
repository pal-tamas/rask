using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class InternalRouteStringAnalyzerTests
{
    [Fact]
    public async Task NavigateTo_with_a_string_literal_matching_a_route_reports_RASK033()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;

                  [Route("todos")]
                  public sealed partial class TodosPage : Component { protected override Component? Render() => null; }

                  public sealed class Menu
                  {
                      public void Go(Navigator nav) => nav.NavigateTo("/todos");
                  }
                  """;

        var d = Assert.Single(await GetDiagnosticsAsync(src));
        Assert.Equal("RASK033", d.Id);
        Assert.Contains("TodosPage", d.GetMessage());
        Assert.Contains("/todos", d.GetMessage());
    }

    [Fact]
    public async Task An_implicit_RouteUrl_conversion_of_a_string_matching_a_route_reports_RASK033()
    {
        // Every RouteUrl slot (NavLink Href:, BsNavItem Href:) is a string → RouteUrl
        // implicit conversion; a local assignment exercises the same conversion the analyzer flags.
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;

                  [Route("todos")]
                  public sealed partial class TodosPage : Component { protected override Component? Render() => null; }

                  public sealed class Menu
                  {
                      public RouteUrl Link() { RouteUrl u = "/todos"; return u; }
                  }
                  """;

        var d = Assert.Single(await GetDiagnosticsAsync(src));
        Assert.Equal("RASK033", d.Id);
        Assert.Contains("TodosPage", d.GetMessage());
    }

    [Fact]
    public async Task NavigateTo_a_path_composed_with_its_parent_route_reports_RASK033()
    {
        // The suggested URL composes the [ParentRoute] chain: Layout "/" + Page "todos" → "/todos".
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;

                  [Route("/")]
                  public sealed class Layout : Component { protected override Component? Render() => null; }

                  [Route("todos")]
                  [ParentRoute(typeof(Layout))]
                  public sealed partial class TodosPage : Component { protected override Component? Render() => null; }

                  public sealed class Menu
                  {
                      public void Go(Navigator nav) => nav.NavigateTo("/todos");
                  }
                  """;

        var d = Assert.Single(await GetDiagnosticsAsync(src));
        Assert.Equal("RASK033", d.Id);
        Assert.Contains("TodosPage", d.GetMessage());
    }

    [Fact]
    public async Task A_secondary_route_template_with_no_formatter_reports_nothing()
    {
        // The generated factory formats a page's FIRST template only ("todos"); the secondary "/todos/new"
        // has no Routes.*() equivalent, so the literal must be left alone.
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;

                  [Route("todos")]
                  [Route("todos/new")]
                  public sealed partial class TodosPage : Component { protected override Component? Render() => null; }

                  public sealed class Menu
                  {
                      public void Go(Navigator nav) => nav.NavigateTo("/todos/new");
                  }
                  """;

        Assert.Empty(await GetDiagnosticsAsync(src));
    }

    [Fact]
    public async Task A_parameterised_route_reports_nothing()
    {
        // "/users/42" maps to a Routes.UserPage("42") the analyzer can't reconstruct from a literal — skip.
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;

                  [Route("users/{id}")]
                  public sealed class UserPage : Component { protected override Component? Render() => null; }

                  public sealed class Menu
                  {
                      public void Go(Navigator nav) => nav.NavigateTo("/users/42");
                  }
                  """;

        Assert.Empty(await GetDiagnosticsAsync(src));
    }

    [Fact]
    public async Task An_external_url_reports_nothing()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;

                  [Route("todos")]
                  public sealed partial class TodosPage : Component { protected override Component? Render() => null; }

                  public sealed class Menu
                  {
                      public void Go(Navigator nav) => nav.NavigateTo("https://example.com/todos");
                  }
                  """;

        Assert.Empty(await GetDiagnosticsAsync(src));
    }

    [Fact]
    public async Task An_unknown_path_reports_nothing()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Demo;

                  [Route("todos")]
                  public sealed partial class TodosPage : Component { protected override Component? Render() => null; }

                  public sealed class Menu
                  {
                      public void Go(Navigator nav) => nav.NavigateTo("/not-a-route");
                  }
                  """;

        Assert.Empty(await GetDiagnosticsAsync(src));
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        string source, string assemblyName = "TestAssembly")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var references = GeneratorDriverFixture.BuildReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new InternalRouteStringAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK033").ToImmutableArray();
    }
}
