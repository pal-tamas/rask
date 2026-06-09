using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators.Tests;

internal static class GeneratorDriverFixture
{
    public static GeneratorRun Run(string source) => Run(source, new ComponentFactoryGenerator());

    public static GeneratorRun RunRoutes(string source) => Run(source, new RoutesGenerator());

    /// <summary>
    ///     Runs <paramref name="generator" /> over multiple named sources plus optional in-memory
    ///     <c>AdditionalText</c> files (scoped <c>.css</c>/<c>.js</c> siblings). Consolidates the
    ///     per-file <c>Run</c>/<c>BuildReferences</c>/<c>InMemoryAdditionalText</c> copies in the
    ///     scoped-asset generator test classes.
    /// </summary>
    public static GeneratorRun Run(
        (string Path, string Source)[] sources,
        IIncrementalGenerator generator,
        (string Path, string Contents)[]? additionalTexts = null)
    {
        var trees = sources
            .Select(s => CSharpSyntaxTree.ParseText(
                s.Source,
                new CSharpParseOptions(LanguageVersion.Latest),
                s.Path))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            trees,
            BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver
            .Create(generator)
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest));

        if (additionalTexts is { Length: > 0 })
        {
            driver = driver.AddAdditionalTexts(additionalTexts
                .Select(t => (AdditionalText)new InMemoryAdditionalText(t.Path, t.Contents))
                .ToImmutableArray());
        }

        var runResult = driver.RunGenerators(compilation).GetRunResult();
        return new GeneratorRun(runResult, compilation);
    }

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

    internal static ImmutableArray<MetadataReference> BuildReferences()
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

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly string _contents;

        public InMemoryAdditionalText(string path, string contents)
        {
            Path = path;
            _contents = contents;
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(_contents);
    }
}

internal sealed record GeneratorRun(GeneratorDriverRunResult RunResult, CSharpCompilation Compilation)
{
    public IEnumerable<Diagnostic> Diagnostics =>
        RunResult.Diagnostics.Concat(RunResult.Results.SelectMany(r => r.Diagnostics));

    /// <summary>
    ///     Compile-error diagnostics (CS####, severity Error) of the input compilation WITH all
    ///     generated sources added back in. Empty ⇒ the generated code is valid C# that compiles
    ///     against the user's sources — the strongest check that no emitted identifier/literal is
    ///     malformed.
    /// </summary>
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
