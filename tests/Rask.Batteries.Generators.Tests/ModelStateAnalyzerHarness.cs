using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Data.Generators.Tests;

/// <summary>
///     Runs one of the model-state analyzers over a source snippet compiled against the real Rask.Data.
/// </summary>
internal static class ModelStateAnalyzerHarness
{
    public static async Task<IReadOnlyList<Diagnostic>> RunAsync(DiagnosticAnalyzer analyzer, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), path: "Source.cs");
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [tree],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        // A snippet that does not compile proves nothing: a symbol that fails to bind is not an entity, so the
        // analyzer would report nothing and read exactly like a passing non-trigger case.
        var errors = compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, "The test source does not compile:\n  " + string.Join("\n  ", errors));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync();

        return diagnostics.OrderBy(static d => d.Location.SourceSpan.Start).ToList();
    }

    /// <summary>The source text a diagnostic points at.</summary>
    public static string Flagged(this Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static ImmutableArray<MetadataReference> References()
    {
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var refs = trusted.Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToList();

        foreach (var name in new[] { "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore" })
        {
            refs.Add(MetadataReference.CreateFromFile(Assembly.Load(name).Location));
        }

        return [.. refs];
    }
}
