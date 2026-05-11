using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Generators.Tests;

internal static class GeneratorDriverFixture
{
    public static GeneratorRun Run(string source) => Run(source, new ComponentFactoryGenerator());

    public static GeneratorRun RunRoutes(string source) => Run(source, new RoutesGenerator());

    private static GeneratorRun Run(string source, IIncrementalGenerator generator)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var references = BuildReferences();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(generator)
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest));

        var result = driver.RunGenerators(compilation);
        var runResult = result.GetRunResult();
        return new GeneratorRun(runResult, compilation);
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var refs = trustedAssemblies
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();

        // Pull Rask.Core in directly (TestAssembly compilation needs to know about Rask.Core.Component).
        var raskCore = Assembly.Load("Rask.Core");
        refs.Add(MetadataReference.CreateFromFile(raskCore.Location));
        return refs.ToImmutableArray();
    }
}

internal sealed record GeneratorRun(GeneratorDriverRunResult RunResult, CSharpCompilation Compilation)
{
    public IEnumerable<Diagnostic> Diagnostics =>
        RunResult.Diagnostics.Concat(RunResult.Results.SelectMany(r => r.Diagnostics));

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
