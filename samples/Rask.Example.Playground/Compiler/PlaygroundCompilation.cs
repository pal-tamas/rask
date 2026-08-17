using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators;

namespace Rask.Example.Playground.Compiler;

/// <summary>
///     The result of parsing a snippet and running the Rask <see cref="ComponentFactoryGenerator" /> over it:
///     the user's own <see cref="SyntaxTree" />, the generator-updated <see cref="Compilation" /> (user tree
///     + the emitted <c>Generated.*</c> factory trees and the two <c>global using static</c> directives), and
///     the generator's own diagnostics. Shared by the Run pipeline (<see cref="PlaygroundCompiler" />) and the
///     live analysis pipeline (<see cref="PlaygroundWorkspace" />) so both see exactly the same bound program —
///     the terse <c>Div()[…]</c> forms resolve identically for execution, diagnostics and completion.
/// </summary>
internal readonly record struct PlaygroundCompilation(
    SyntaxTree UserTree,
    Compilation Output,
    ImmutableArray<Diagnostic> GeneratorDiagnostics)
{
    // Shared so parse behaviour matches across paths; the generator driver is created with the same options.
    public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    /// <summary>
    ///     Parse <paramref name="source" /> and run only the factory generator (the routes generator is
    ///     deliberately skipped — playground snippets have no <c>[Route]</c>, and skipping it avoids its
    ///     <c>[ModuleInitializer]</c> touching the host's shared <c>RouteRegistry</c>). <paramref name="assemblyName" />
    ///     must be unique per Run compile (Mono WASM never unloads an <c>Assembly.Load</c>ed image), but is
    ///     irrelevant for analysis passes that never Emit.
    /// </summary>
    public static PlaygroundCompilation Create(
        string source,
        IEnumerable<MetadataReference> references,
        string assemblyName,
        CancellationToken cancellationToken)
    {
        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions, cancellationToken: cancellationToken);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                optimizationLevel: OptimizationLevel.Debug));

        var driver = CSharpGeneratorDriver
            .Create(new ComponentFactoryGenerator())
            .WithUpdatedParseOptions(ParseOptions)
            // Turn the builder surface on, exactly as a real project build does via the MSBuild property.
            // It used not to matter here: the HTML tags lived in Rask.Core and their entries arrived by
            // INHERITANCE from RaskMarkup, already compiled into the referenced Rask.Core.dll. They ship
            // from Rask.Html now, and a referenced library's entries are INJECTED — which this emission
            // does, and which is skipped entirely when the property is absent. Without this a snippet
            // that writes `Input.Bind(…)` no longer compiles in the playground.
            .WithUpdatedAnalyzerConfigOptions(BuilderSurfaceOptions.Instance);

        driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var output, out var generatorDiagnostics, cancellationToken);

        return new PlaygroundCompilation(tree, output, generatorDiagnostics);
    }

    /// <summary>The generator-emitted trees only (everything in the output except the user's own tree).</summary>
    public IEnumerable<SyntaxTree> GeneratedTrees()
    {
        var userTree = UserTree; // struct members can't be captured by the lambda; copy to a local first.
        return Output.SyntaxTrees.Where(t => t != userTree);
    }

    /// <summary>
    ///     The single MSBuild property the Rask generator reads here: <c>RaskBuilderSurface</c>. A real build
    ///     gets it from <c>Rask.Core.targets</c> (<c>CompilerVisibleProperty</c>); the playground has no
    ///     MSBuild, so it is stated directly.
    /// </summary>
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
}
