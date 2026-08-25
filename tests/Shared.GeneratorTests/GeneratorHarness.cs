using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.TestSupport;

/// <summary>
/// Drives an <see cref="IIncrementalGenerator"/> over an in-memory compilation.
/// </summary>
/// <remarks>
/// Linked into each <c>*.Generators.Tests</c> project rather than shared through a project reference, so
/// every generator suite asserts against exactly the same harness. Driving the generator by hand (instead
/// of referencing it as an <c>Analyzer</c>) is what makes the negative cases testable at all: a source
/// shape that makes the generator emit non-compiling code would otherwise break the test project's own
/// build, so it could never be asserted on.
/// </remarks>
internal static class GeneratorHarness
{
    /// <summary>
    /// Runs <paramref name="generator"/> over <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The C# source to compile and feed to the generator.</param>
    /// <param name="generator">The generator under test.</param>
    /// <param name="extraAssemblies">
    /// Simple names of assemblies the source — or the <i>generated</i> code — needs to compile, so
    /// <see cref="GeneratorRun.GeneratedCompileErrors"/> can validate the emitted code for real.
    /// </param>
    public static GeneratorRun Run(string source, IIncrementalGenerator generator, params string[] extraAssemblies) =>
        Run(source, generator, globalOptions: null, extraAssemblies);

    /// <summary>
    /// Runs <paramref name="generator"/> with MSBuild properties visible to it.
    /// </summary>
    /// <param name="globalOptions">
    /// The analyzer-config global options, keyed the way a generator reads them —
    /// <c>build_property.Something</c>. A generator branch gated on one of these is otherwise
    /// untestable: the default provider is empty, so only the off path would ever be exercised.
    /// </param>
    public static GeneratorRun Run(
        string source,
        IIncrementalGenerator generator,
        IReadOnlyDictionary<string, string>? globalOptions,
        params string[] extraAssemblies)
    {
        // Give the tree a real path. Roslyn scopes `file`-local types by syntax-tree path, so trees that
        // all share the default empty path are treated as one file — which would silently hide exactly
        // the cross-file visibility errors these suites exist to catch.
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source, new CSharpParseOptions(LanguageVersion.Latest), path: "Source.cs");
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            BuildReferences(extraAssemblies),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest));

        if (globalOptions is not null)
        {
            driver = driver.WithUpdatedAnalyzerConfigOptions(new FixedAnalyzerConfigOptionsProvider(globalOptions));
        }

        return new GeneratorRun(driver.RunGenerators(compilation).GetRunResult(), compilation);
    }

    /// <summary>The smallest provider that answers <c>GlobalOptions</c> and nothing else.</summary>
    private sealed class FixedAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> options)
        : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new FixedAnalyzerConfigOptions(options);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }

    private sealed class FixedAnalyzerConfigOptions(IReadOnlyDictionary<string, string> options) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) =>
            options.TryGetValue(key, out value);
    }

    private static ImmutableArray<MetadataReference> BuildReferences(params string[] extraAssemblies)
    {
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var refs = trusted
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        foreach (var name in extraAssemblies)
        {
            refs.Add(MetadataReference.CreateFromFile(Assembly.Load(name).Location));
        }

        return refs.ToImmutableArray();
    }
}

/// <summary>The result of one generator run, with the three assertion surfaces the suites need.</summary>
internal sealed record GeneratorRun(GeneratorDriverRunResult RunResult, CSharpCompilation Compilation)
{
    /// <summary>
    /// Every diagnostic the generators reported. <see cref="GeneratorDriverRunResult.Diagnostics"/> is
    /// already the aggregate of each result's diagnostics, so concatenating the per-result collections
    /// on top would report each one twice and break any exact-count assertion.
    /// </summary>
    public IEnumerable<Diagnostic> Diagnostics => RunResult.Diagnostics;

    /// <summary>
    /// Compile errors from adding every generated source back into the compilation — the direct test for
    /// "the generator emitted code that doesn't build".
    /// </summary>
    public IReadOnlyList<Diagnostic> GeneratedCompileErrors()
    {
        // Parse each generated source under its own hint name, for the same reason the input tree gets a
        // path: generated code lives in a different file from the user's, and only distinct paths make
        // the compiler enforce that.
        var generated = RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => CSharpSyntaxTree.ParseText(
                s.SourceText, new CSharpParseOptions(LanguageVersion.Latest), path: s.HintName))
            .ToArray();

        return Compilation.AddSyntaxTrees(generated)
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
    }

    /// <summary>The text of the generated source whose hint name contains <paramref name="hintNameContains"/>.</summary>
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

    /// <summary>True when any generated source has a hint name containing <paramref name="hintNameContains"/>.</summary>
    public bool HasGeneratedSource(string hintNameContains) =>
        RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Any(s => s.HintName.Contains(hintNameContains, StringComparison.Ordinal));
}
