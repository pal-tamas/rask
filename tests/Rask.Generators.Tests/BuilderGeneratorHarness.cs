using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Tests;

// Runs ComponentFactoryGenerator with the builder surface switched ON (it is opt-in behind the
// RaskBuilderSurface MSBuild property, so the driver has to say so) and hands back both halves of the
// result: the generated sources and the diagnostics. Shared by every builder-surface test — the setter
// emission, the entry emission and the diagnostics all read the same run.
internal static class BuilderGeneratorHarness
{
    internal static BuilderRun Run(string source) => Run(source, null);

    /// <summary>
    ///     The same run with extra references — an emitted library compilation, for the cross-assembly
    ///     entry scan. The assembly name stays <c>TestAssembly</c>, which is what a library's
    ///     <c>InternalsVisibleTo</c> has to name for the friend path to be exercised.
    /// </summary>
    internal static BuilderRun Run(string source, IEnumerable<MetadataReference>? extraReferences)
    {
        var references = GeneratorDriverFixture.BuildReferences();
        if (extraReferences is not null)
        {
            references = references.AddRange(extraReferences);
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = (CSharpGeneratorDriver)CSharpGeneratorDriver
            .Create(new ComponentFactoryGenerator())
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .WithUpdatedAnalyzerConfigOptions(new BuilderSurfaceOptionsProvider());

        var result = driver.RunGenerators(compilation).GetRunResult();
        return new BuilderRun(
            result.Results.SelectMany(r => r.GeneratedSources).ToImmutableArray(),
            result.Diagnostics);
    }

    /// <summary>
    ///     The compilation as the compiler sees it AFTER the factory generator has run with the builder
    ///     surface on — source plus generated trees. Analyzer tests need this: the chain syntax
    ///     (<c>Div[Span]</c>) binds to generated entries, so analyzing the bare source would leave every chain
    ///     expression an error type and any rule under test would report nothing and pass for the wrong reason.
    /// </summary>
    internal static Compilation Compile(string source, string assemblyName = "TestAssembly")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver
            .Create(new ComponentFactoryGenerator())
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .WithUpdatedAnalyzerConfigOptions(new BuilderSurfaceOptionsProvider())
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);

        return generated;
    }

    internal readonly record struct BuilderRun(
        ImmutableArray<GeneratedSourceResult> Sources,
        ImmutableArray<Diagnostic> Diagnostics)
    {
        internal string Source(string hintName)
        {
            var match = Sources.FirstOrDefault(s => s.HintName.Contains(hintName, StringComparison.Ordinal));
            if (match.SourceText is null)
            {
                throw new InvalidOperationException(
                    $"No {hintName} generated. Available: ["
                    + string.Join(", ", Sources.Select(s => s.HintName)) + "]");
            }

            return match.SourceText.ToString();
        }

        internal IEnumerable<Diagnostic> WithId(string id) =>
            Diagnostics.Where(d => string.Equals(d.Id, id, StringComparison.Ordinal));
    }

    // The generated methods are brace-per-line at a fixed indent, so a body runs to the first "    }".
    internal static string Method(string output, string name)
    {
        var start = output.IndexOf(" " + name + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, name + " was not emitted");
        var end = output.IndexOf("\n    }", start, StringComparison.Ordinal);
        return output.Substring(start, end - start);
    }

    private sealed class BuilderSurfaceOptionsProvider : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options();

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Options : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                if (key == "build_property.RaskBuilderSurface")
                {
                    value = "true";
                    return true;
                }

                value = string.Empty;
                return false;
            }
        }
    }
}
