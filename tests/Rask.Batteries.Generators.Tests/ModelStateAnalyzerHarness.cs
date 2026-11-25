using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
///     Runs one of the model-state analyzers over a source snippet compiled against the real Rask.Data.
/// </summary>
internal static class ModelStateAnalyzerHarness
{
    /// <param name="analyzer">The analyzer to run.</param>
    /// <param name="source">The snippet, which must compile.</param>
    /// <param name="options">Changes the compilation's options — a severity an editorconfig would set, say — or null.</param>
    /// <param name="path">The file the snippet is compiled as, for a rule that reads where code is written.</param>
    public static async Task<IReadOnlyList<Diagnostic>> RunAsync(
        DiagnosticAnalyzer analyzer, string source, Func<CSharpCompilationOptions, CSharpCompilationOptions>? options = null, string path = "Source.cs")
    {
        var diagnostics = await Compile(source, options, path)
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync();

        return diagnostics.OrderBy(static d => d.Location.SourceSpan.Start).ToList();
    }

    /// <summary>Compiles <paramref name="source" /> against the real Rask.Data, failing the test when it does not compile.</summary>
    public static CSharpCompilation Compile(
        string source, Func<CSharpCompilationOptions, CSharpCompilationOptions>? options = null, string path = "Source.cs")
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), path: path);
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [tree],
            References(),
            options is null ? compilationOptions : options(compilationOptions));

        // A snippet that does not compile proves nothing: a symbol that fails to bind is not an entity, so the
        // analyzer would report nothing and read exactly like a passing non-trigger case.
        var errors = compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, "The test source does not compile:\n  " + string.Join("\n  ", errors));
        return compilation;
    }

    /// <summary>The source text a diagnostic points at.</summary>
    public static string Flagged(this Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    // Once per process: Roslyn shares loaded metadata only between compilations handed the SAME reference instances.
    private static readonly ImmutableArray<MetadataReference> CachedReferences = CreateReferences();

    private static ImmutableArray<MetadataReference> References() => CachedReferences;

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var refs = trusted.Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToList();

        foreach (var name in new[] { "Rask.Data", "Rask.Cqrs", "Rask.Jobs", "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Relational", "FluentValidation" })
        {
            refs.Add(MetadataReference.CreateFromFile(Assembly.Load(name).Location));
        }

        return [.. refs];
    }
}
