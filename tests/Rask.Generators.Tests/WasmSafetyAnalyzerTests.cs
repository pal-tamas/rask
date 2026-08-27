using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

// A page that reaches a database cannot run in a browser, so it stays server-live. That is the
// correct outcome rather than a fault, which is why this is Info: for most apps it is every data
// page, and a warning on each of them would be noise nobody reads.
//
// The negative cases matter more than the positive one here. An analyzer that fires on ordinary
// server code is worse than no analyzer, because it trains people to ignore it.
public class WasmSafetyAnalyzerTests
{
    [Fact]
    public async Task ARoutedPageInjectingADbContextFactory_ReportsRask054()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  using Microsoft.EntityFrameworkCore;

                  [Route("/orders")]
                  public sealed partial class Orders(IDbContextFactory<AppDb> db) : Component
                  {
                      protected override Component? Render() => null;
                  }

                  public sealed class AppDb : DbContext { }

                  namespace Microsoft.EntityFrameworkCore
                  {
                      // Declared here rather than referenced: the analyzer matches by full name, and the
                      // generator test harness deliberately carries no EF Core reference. This keeps the
                      // rule under test without dragging a data stack into an analyzer suite.
                      public class DbContext { }
                      public interface IDbContextFactory<T> { }
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src);

        var d = Assert.Single(diagnostics);
        Assert.Equal("RASK054", d.Id);
        Assert.Contains("Orders", d.GetMessage());
        Assert.Equal(DiagnosticSeverity.Info, d.Severity);
    }

    [Fact]
    public async Task ARoutedPageInjectingADbContextDirectly_ReportsRask054()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  using Microsoft.EntityFrameworkCore;

                  [Route("/orders")]
                  public sealed partial class Orders(AppDb db) : Component
                  {
                      protected override Component? Render() => null;
                  }

                  public sealed class AppDb : DbContext { }

                  namespace Microsoft.EntityFrameworkCore
                  {
                      // Declared here rather than referenced: the analyzer matches by full name, and the
                      // generator test harness deliberately carries no EF Core reference. This keeps the
                      // rule under test without dragging a data stack into an analyzer suite.
                      public class DbContext { }
                      public interface IDbContextFactory<T> { }
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src);

        Assert.Single(diagnostics);
    }

    [Fact]
    public async Task ARoutedPageInjectingNothingServerOnly_IsSilent()
    {
        // The shape the analyzer is steering towards: data through something that already crosses the
        // wire. This page is eligible to move, and saying nothing is how it says so.
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;

                  public interface IOrders { }

                  [Route("/orders")]
                  public sealed partial class Orders(IOrders orders) : Component
                  {
                      protected override Component? Render() => null;
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AComponentThatIsNotARoutedPage_IsSilent()
    {
        // A shared component injecting a DbContext is a property of whatever page uses it. Reporting
        // it here would point at a file whose author cannot see which page it affects.
        var src = """
                  using Rask.Core;
                  using Microsoft.EntityFrameworkCore;

                  public sealed partial class OrdersPanel(AppDb db) : Component
                  {
                      protected override Component? Render() => null;
                  }

                  public sealed class AppDb : DbContext { }

                  namespace Microsoft.EntityFrameworkCore
                  {
                      // Declared here rather than referenced: the analyzer matches by full name, and the
                      // generator test harness deliberately carries no EF Core reference. This keeps the
                      // rule under test without dragging a data stack into an analyzer suite.
                      public class DbContext { }
                      public interface IDbContextFactory<T> { }
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task APlainClassThatIsNotAComponent_IsSilent()
    {
        var src = """
                  using Microsoft.EntityFrameworkCore;

                  public sealed class OrderService(AppDb db)
                  {
                      public AppDb Db => db;
                  }

                  public sealed class AppDb : DbContext { }

                  namespace Microsoft.EntityFrameworkCore
                  {
                      // Declared here rather than referenced: the analyzer matches by full name, and the
                      // generator test harness deliberately carries no EF Core reference. This keeps the
                      // rule under test without dragging a data stack into an analyzer suite.
                      public class DbContext { }
                      public interface IDbContextFactory<T> { }
                  }
                  """;

        var diagnostics = await GetDiagnosticsAsync(src);

        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source,
        string assemblyName = "TestAssembly")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var references = GeneratorDriverFixture.BuildReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new WasmSafetyAnalyzer());
        var withAnalyzers = compilation.WithAnalyzers(analyzers);
        var all = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK054").ToImmutableArray();
    }
}
