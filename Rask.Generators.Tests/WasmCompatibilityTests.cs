using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Generators.Tests;

// Regression: the source generator must emit code that compiles against a WASM-style
// reference set — i.e., without the Microsoft.AspNetCore.App shared framework. The WASM
// runtime cannot consume that framework reference, so any inadvertent AspNetCore type in
// the emission would break Rask.Wasm consumers.
public class WasmCompatibilityTests
{
    [Fact]
    public void EmittedFactory_ContainsNoAspNetCoreTypes()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string Name { get; set; }
                      public string? Subtitle { get; set; }
                      public override Component Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run(src);
        var output = run.GeneratedSource("Demo.Components.g.cs");

        Assert.DoesNotContain("Microsoft.AspNetCore", output);
        Assert.DoesNotContain("HttpContext", output);
        Assert.DoesNotContain("WebSocket", output);
    }

    [Fact]
    public void EmittedFactory_CompilesAgainstWasmReferenceSet()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Widget : Component
                  {
                      public string Name { get; set; }
                      protected override Component Render() => this;
                  }
                  """;

        var syntaxTree = CSharpSyntaxTree.ParseText(src);
        var compilation = CSharpCompilation.Create(
            "WasmCompat",
            new[] { syntaxTree },
            BuildWasmStyleReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new ComponentFactoryGenerator());
        var result = driver.RunGenerators(compilation).GetRunResult();
        var generated = result.Results.SelectMany(r => r.GeneratedSources).ToList();
        Assert.NotEmpty(generated);

        var combined = compilation.AddSyntaxTrees(generated.Select(s => s.SyntaxTree));
        var diagnostics = combined.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(diagnostics.Count == 0,
            "Generator output failed to compile against WASM reference set:\n" +
            string.Join("\n", diagnostics.Select(d => d.ToString())));
    }

    private static ImmutableArray<MetadataReference> BuildWasmStyleReferences()
    {
        // Filter the test runtime's assemblies to those a net10.0-browser project would have.
        // Excludes Microsoft.AspNetCore.* (the shared framework that WASM cannot consume).
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var refs = trusted
            .Where(path =>
                !Path.GetFileName(path).StartsWith("Microsoft.AspNetCore.", StringComparison.OrdinalIgnoreCase))
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();

        var raskCore = Assembly.Load("Rask.Core");
        refs.Add(MetadataReference.CreateFromFile(raskCore.Location));
        return refs.ToImmutableArray();
    }
}
