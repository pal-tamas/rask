using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Generators;

namespace Rask.Example.Playground.Compiler;

/// <summary>
///     Compiles a snippet of Rask component C# into an assembly entirely in-process — the same
///     Roslyn pipeline the SDK runs at build time, minus the Razor transpile step Blazor needs:
///     <c>parse → run the Rask ComponentFactoryGenerator → Emit → Assembly.Load → instantiate</c>.
///     Because the Rask source generator runs here, the emitted assembly gets its
///     <c>Generated.Div(...)</c> factories and the <c>global using static Rask.Core.Components.Generated;</c>
///     that make the terse <c>Div()[…]</c> forms resolve — so a visitor writes exactly the code they'd
///     write in a real project. A second, display-only pass runs Rask's analyzers (RASK0##) so the
///     framework's diagnostics can surface as editor squiggles; analyzer/generator diagnostics never
///     gate execution — only CS compile errors and a failed <c>Emit</c> do.
/// </summary>
/// <remarks>
///     The reference set (BCL + Rask.Core + Rask.Bootstrap …) is supplied by the host: the desktop unit
///     tests build it from real files, the browser host downloads the shipped <c>_framework/*.dll</c> and
///     wraps them via <c>MetadataReference.CreateFromStream</c> (no real filesystem in WASM). Each compile
///     uses a fresh assembly name — Mono WASM can't unload an <c>Assembly.Load</c>ed assembly, so repeated
///     runs accumulate; that's an accepted playground trade-off (reload the tab to reclaim).
/// </remarks>
public sealed class PlaygroundCompiler
{
    // Rask's analyzers live in the generator assembly; instantiate every concrete DiagnosticAnalyzer once
    // and reuse across compiles. Purely for surfacing RASK0## hints — they do not affect what runs.
    private static readonly ImmutableArray<DiagnosticAnalyzer> RaskAnalyzers = DiscoverAnalyzers();

    private readonly IReadOnlyList<MetadataReference> _references;
    private readonly IServiceProvider _services;
    private int _counter;

    public PlaygroundCompiler(IReadOnlyList<MetadataReference> references, IServiceProvider services)
    {
        _references = references;
        _services = services;
    }

    public async Task<PlaygroundResult> CompileAsync(string source, CancellationToken cancellationToken = default)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions, cancellationToken: cancellationToken);

        // Unique per compile: two assemblies sharing an identity would clash on Assembly.Load, and Mono
        // WASM never unloads, so a monotonic suffix keeps each run distinct.
        var assemblyName = "RaskPlayground_" + Interlocked.Increment(ref _counter);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            _references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                optimizationLevel: OptimizationLevel.Debug));

        // Run only the factory generator: it emits the per-component factories AND the two core
        // `global using static` directives, which is everything user code needs to resolve. The routes
        // generator is deliberately skipped — playground snippets have no [Route], and skipping it avoids
        // its [ModuleInitializer] touching the host's shared RouteRegistry.
        var driver = CSharpGeneratorDriver
            .Create(new ComponentFactoryGenerator())
            .WithUpdatedParseOptions(parseOptions);

        driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var output, out var generatorDiagnostics, cancellationToken);

        // Bind once — on the single-threaded WASM interpreter a second GetDiagnostics() would re-bind the
        // whole compilation. Reused for both the display list and the error gate below.
        var compilationDiagnostics = output.GetDiagnostics(cancellationToken);

        var diagnostics = new List<PlaygroundDiagnostic>();
        CollectUserDiagnostics(generatorDiagnostics, tree, diagnostics);
        CollectUserDiagnostics(compilationDiagnostics, tree, diagnostics);

        // Display-only lint pass. Best-effort: an analyzer that throws on the bespoke playground setup
        // must not sink the whole compile, so failures just mean no RASK squiggles this run.
        await AppendAnalyzerDiagnosticsAsync(output, tree, diagnostics, cancellationToken).ConfigureAwait(false);

        // Only CS compile errors (or a failed Emit) block execution — Rask's RASK0## hints are advisory.
        if (compilationDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new PlaygroundResult(null, diagnostics, Succeeded: false);
        }

        using var peStream = new MemoryStream();
        var emit = output.Emit(peStream, cancellationToken: cancellationToken);
        if (!emit.Success)
        {
            foreach (var d in emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(Map(d));
            }

            return new PlaygroundResult(null, diagnostics, false);
        }

        var assembly = Assembly.Load(peStream.ToArray());
        var entry = FindEntryComponent(assembly, out var ambiguous);
        if (entry is null)
        {
            diagnostics.Add(Synthetic(ambiguous
                ? "Multiple components found — name your entry component 'Playground' so the playground knows " +
                  "which to render (e.g. `public sealed class Playground : Component`)."
                : "No public component found. Define a component, e.g. `public sealed class Playground : Component`."));
            return new PlaygroundResult(null, diagnostics, false);
        }

        try
        {
            var component = (Component)ActivatorUtilities.CreateInstance(_services, entry);
            return new PlaygroundResult(component, diagnostics, true);
        }
        catch (Exception ex)
        {
            // Surface the innermost failure — a throwing constructor arrives wrapped in a
            // TargetInvocationException, whose message ("Exception has been thrown by the target of an
            // invocation") is useless on its own.
            var root = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            diagnostics.Add(Synthetic($"Constructing '{entry.Name}' threw: {root.Message}"));
            return new PlaygroundResult(null, diagnostics, false);
        }
    }

    private static void CollectUserDiagnostics(
        IEnumerable<Diagnostic> source, SyntaxTree userTree, List<PlaygroundDiagnostic> into)
    {
        foreach (var d in source)
        {
            if (d.Severity == DiagnosticSeverity.Hidden)
            {
                continue;
            }

            // Anchor to the user's source: a diagnostic pointing into a generated tree would reference
            // lines the visitor never wrote. Location-less diagnostics (global) are still shown.
            if (d.Location.SourceTree is { } st && st != userTree)
            {
                continue;
            }

            into.Add(Map(d));
        }
    }

    private async Task AppendAnalyzerDiagnosticsAsync(
        Compilation output, SyntaxTree userTree, List<PlaygroundDiagnostic> into, CancellationToken cancellationToken)
    {
        if (RaskAnalyzers.IsDefaultOrEmpty)
        {
            return;
        }

        try
        {
            var withAnalyzers = output.WithAnalyzers(RaskAnalyzers, options: null);
            var analyzerDiagnostics = await withAnalyzers
                .GetAnalyzerDiagnosticsAsync(cancellationToken)
                .ConfigureAwait(false);
            CollectUserDiagnostics(analyzerDiagnostics, userTree, into);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Advisory pass only — swallow so a misbehaving analyzer can't break a valid compile.
        }
    }

    private static ImmutableArray<DiagnosticAnalyzer> DiscoverAnalyzers()
    {
        try
        {
            return typeof(ComponentFactoryGenerator).Assembly
                .GetTypes()
                .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                .Select(t => (DiagnosticAnalyzer?)Activator.CreateInstance(t))
                .Where(a => a is not null)
                .Select(a => a!)
                .ToImmutableArray();
        }
        catch
        {
            return ImmutableArray<DiagnosticAnalyzer>.Empty;
        }
    }

    private static Type? FindEntryComponent(Assembly assembly, out bool ambiguous)
    {
        ambiguous = false;

        var candidates = assembly
            .GetTypes()
            .Where(t => t is { IsPublic: true, IsAbstract: false } && typeof(Component).IsAssignableFrom(t))
            .ToList();

        // A component literally named "Playground" is always the entry point (what the starter + docs use).
        var named = candidates.FirstOrDefault(t => t.Name == "Playground");
        if (named is not null)
        {
            return named;
        }

        // Otherwise a single component is unambiguous; with several and none named Playground the caller
        // must disambiguate — picking one by reflection order would render an arbitrary (often child) one.
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        ambiguous = candidates.Count > 1;
        return null;
    }

    private static PlaygroundDiagnostic Map(Diagnostic d)
    {
        var span = d.Location.GetLineSpan();
        var start = span.StartLinePosition;
        var end = span.EndLinePosition;
        var severity = d.Severity switch
        {
            DiagnosticSeverity.Error => PlaygroundSeverity.Error,
            DiagnosticSeverity.Warning => PlaygroundSeverity.Warning,
            _ => PlaygroundSeverity.Info
        };

        // Roslyn LinePosition is 0-based; Monaco markers are 1-based.
        return new PlaygroundDiagnostic(
            d.Id, severity, d.GetMessage(),
            start.Line + 1, start.Character + 1,
            end.Line + 1, end.Character + 1);
    }

    private static PlaygroundDiagnostic Synthetic(string message) =>
        new("RASKPLAY", PlaygroundSeverity.Error, message, 1, 1, 1, 1);
}
