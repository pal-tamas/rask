using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Cqrs.Generators.Tests;

internal static class CqrsGeneratorFixture
{
    public static GeneratorRun Run(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new CqrsDispatchGenerator())
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest));

        return new GeneratorRun(driver.RunGenerators(compilation).GetRunResult(), compilation);
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var refs = trusted
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        // The source under test implements Rask.Cqrs interfaces, and the generated code calls into
        // Rask.Cqrs + Microsoft.Extensions.DependencyInjection — pull both in so GeneratedCompileErrors
        // can validate that the emitted code actually compiles.
        refs.Add(MetadataReference.CreateFromFile(Assembly.Load("Rask.Cqrs").Location));
        refs.Add(MetadataReference.CreateFromFile(
            Assembly.Load("Microsoft.Extensions.DependencyInjection.Abstractions").Location));
        return refs.ToImmutableArray();
    }
}

internal sealed record GeneratorRun(GeneratorDriverRunResult RunResult, CSharpCompilation Compilation)
{
    public IEnumerable<Diagnostic> Diagnostics =>
        RunResult.Diagnostics.Concat(RunResult.Results.SelectMany(r => r.Diagnostics));

    public IReadOnlyList<Diagnostic> GeneratedCompileErrors()
    {
        var generated = RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText, new CSharpParseOptions(LanguageVersion.Latest)))
            .ToArray();

        return Compilation.AddSyntaxTrees(generated)
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
    }

    public string GeneratedSource(string hintNameContains)
    {
        foreach (var result in RunResult.Results)
        {
            foreach (var src in result.GeneratedSources)
            {
                if (src.HintName.Contains(hintNameContains, StringComparison.Ordinal))
                {
                    return src.SourceText.ToString();
                }
            }
        }

        var available = string.Join(", ",
            RunResult.Results.SelectMany(r => r.GeneratedSources).Select(s => s.HintName));
        throw new InvalidOperationException(
            $"No generated source found containing '{hintNameContains}'. Available: [{available}]");
    }
}
