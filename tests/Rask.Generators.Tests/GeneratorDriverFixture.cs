using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
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
        (string Path, string Contents)[]? additionalTexts = null) =>
        Run(sources, [generator], additionalTexts);

    /// <summary>
    ///     Runs SEVERAL generators over one compilation, so their outputs are compiled together.
    /// </summary>
    /// <remarks>
    ///     Needed whenever the question is whether generated code BINDS rather than whether it was
    ///     emitted. Asserting on the generated text alone is a false negative in this repo: inside a
    ///     markup host the factory generator injects builder entries as members of the host type, and
    ///     ordinary member lookup beats the namespace-level names another generator produced. Only a
    ///     combined compilation can show which one a call site actually resolves to.
    /// </remarks>
    /// <summary>
    ///     Runs the scoped-asset generator the way the build actually drives it: over COMPILED
    ///     output, tagged with the <c>.ts</c> it came from.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A source generator cannot run a compiler, so <c>Rask.Core.targets</c> compiles each
    ///         sibling <c>.ts</c> into <c>obj/</c> and hands csc the result with a
    ///         <c>RaskTsSource</c> metadata item naming the original. Every pairing decision and every
    ///         diagnostic location keys off that tag, so a test that passed the <c>.ts</c> path
    ///         directly would exercise a path the build never takes.
    ///     </para>
    ///     <para>
    ///         Callers still write <c>.ts</c> paths, which is what the author writes; the obj-side
    ///         path is derived here so no test has to know the layout.
    ///     </para>
    /// </remarks>
    /// <param name="scopedTs">The author's <c>.ts</c> files, as (path, COMPILED contents).</param>
    /// <param name="strayJs">
    ///     Paths for <c>RaskStrayScopedJs</c> — the <c>.js</c> siblings MSBuild found but never
    ///     compiled, which is how RASK054 is reported without csc ever opening them.
    /// </param>
    public static GeneratorRun RunScoped(
        (string Path, string Source)[] sources,
        (string Path, string Contents)[] scopedTs,
        string[]? strayJs = null)
    {
        var compiled = scopedTs
            .Select(t => (Path: CompiledPathFor(t.Path), t.Contents, Source: t.Path))
            .ToArray();

        var options = new ScopedAssetOptions(
            compiled.ToDictionary(c => c.Path, c => c.Source, StringComparer.Ordinal),
            strayJs is { Length: > 0 } ? string.Join(";", strayJs) : null);

        return Run(
            sources,
            [new ComponentScopedJsGenerator()],
            compiled.Select(c => (c.Path, c.Contents)).ToArray(),
            options);
    }

    /// <summary>Where the build writes one scoped file's compiled output.</summary>
    private static string CompiledPathFor(string tsPath)
    {
        var directory = System.IO.Path.GetDirectoryName(tsPath)?.Replace('\\', '/') ?? string.Empty;
        var stem = System.IO.Path.GetFileNameWithoutExtension(tsPath);
        return $"/obj/rask/ts{directory}/{stem}.js";
    }

    public static GeneratorRun Run(
        (string Path, string Source)[] sources,
        IIncrementalGenerator[] generators,
        (string Path, string Contents)[]? additionalTexts = null,
        AnalyzerConfigOptionsProvider? analyzerConfigOptions = null)
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
            .Create(generators)
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest));

        if (additionalTexts is { Length: > 0 })
        {
            driver = driver.AddAdditionalTexts(additionalTexts
                .Select(t => (AdditionalText)new InMemoryAdditionalText(t.Path, t.Contents))
                .ToImmutableArray());
        }

        if (analyzerConfigOptions is not null)
        {
            driver = driver.WithUpdatedAnalyzerConfigOptions(analyzerConfigOptions);
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

    /// <summary>
    ///     Runs the factory generator over <paramref name="compilation" /> with the builder surface on, and
    ///     returns the compilation the analyzers should actually see.
    /// </summary>
    /// <remarks>
    ///     A plain compilation has no builder entries for a tag that ships from Rask.Html: the framework's
    ///     own entries used to be precompiled into Rask.Core.dll and arrive by inheritance, so an analyzer
    ///     test could bind `Img.Src(…)` without a generator ever running. A referenced library's entries are
    ///     INJECTED instead, which only exists once the generator has run — so a chain over a moved tag
    ///     silently failed to bind and the analyzer saw nothing to report.
    /// </remarks>
    internal static Compilation WithBuilderSurface(Compilation compilation)
    {
        var driver = CSharpGeneratorDriver
            .Create(new ComponentFactoryGenerator())
            .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
            .WithUpdatedAnalyzerConfigOptions(BuilderSurfaceOptions.Instance);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return output;
    }

    /// <summary>
    ///     Per-file metadata, which the other options providers here cannot express.
    /// </summary>
    /// <remarks>
    ///     Every existing provider in this fixture answers <c>GetOptions(AdditionalText)</c> with the
    ///     GLOBAL options, which is fine while the only thing a generator reads is a property. The
    ///     scoped-asset generator reads <c>RaskTsSource</c> per file — the whole mechanism by which a
    ///     compiled artefact in obj/ is traced back to the <c>.ts</c> beside the component — so a
    ///     shared answer would hand every file the same source path and pair them all against one
    ///     component.
    /// </remarks>
    private sealed class ScopedAssetOptions(
        Dictionary<string, string> tsSourceByCompiledPath,
        string? strayJs) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Global(strayJs);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
            tsSourceByCompiledPath.TryGetValue(textFile.Path, out var source)
                ? new PerFile(source)
                : GlobalOptions;

        private sealed class PerFile(string tsSource) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                if (string.Equals(key, "build_metadata.AdditionalFiles.RaskTsSource", StringComparison.Ordinal))
                {
                    value = tsSource;
                    return true;
                }

                value = null!;
                return false;
            }
        }

        private sealed class Global(string? strayJs) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                if (strayJs is not null
                    && string.Equals(key, "build_property.RaskStrayScopedJs", StringComparison.Ordinal))
                {
                    value = strayJs;
                    return true;
                }

                value = null!;
                return false;
            }
        }
    }

    // The generator reads RaskBuilderSurface from MSBuild; these tests have none, so it is stated here.
    private sealed class BuilderSurfaceOptions : AnalyzerConfigOptionsProvider
    {
        internal static readonly BuilderSurfaceOptions Instance = new();

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options();

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Options : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                if (string.Equals(key, "build_property.RaskBuilderSurface", StringComparison.Ordinal))
                {
                    value = "true";
                    return true;
                }

                value = null!;
                return false;
            }
        }
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

        // Rask.Html, which now declares the HTML/SVG element family — the test snippets are full of
        // Div/Span/Img/Input, and the analyzers that pin a tag by full metadata name resolve it here.
        var raskHtml = Assembly.Load("Rask.Html");
        refs.Add(MetadataReference.CreateFromFile(raskHtml.Location));

        // Rask.Server too, so analyzer tests can resolve the real UseRask symbol (the ASP.NET Core
        // shared framework rides along in the trusted-platform-assemblies set above via the project's
        // transitive framework reference, giving UseAuthentication / WebApplication as well).
        var raskServer = Assembly.Load("Rask.Server");
        refs.Add(MetadataReference.CreateFromFile(raskServer.Location));

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
