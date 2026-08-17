using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;

namespace Rask.Example.Playground.Compiler;

/// <summary>
///     Compiles a snippet of Rask component C# into an assembly entirely in-process — the same
///     Roslyn pipeline the SDK runs at build time, minus the Razor transpile step Blazor needs:
///     <c>parse → run the Rask ComponentFactoryGenerator → Emit → Assembly.Load → instantiate</c>.
///     Because the Rask source generator runs here, the emitted assembly gets the builder entries that make
///     a chain like <c>Div.Class("card")[…]</c> resolve — including entries for the visitor's own components,
///     which is why the driver has to switch the builder surface on itself (see
///     <see cref="PlaygroundCompilation" />) — so a visitor writes exactly the code they'd write in a real
///     project. A second, display-only pass runs Rask's analyzers (RASK0##) so the
///     framework's diagnostics can surface as editor squiggles; analyzer/generator diagnostics never
///     gate execution — only CS compile errors and a failed <c>Emit</c> do.
/// </summary>
/// <remarks>
///     The reference set (BCL + Rask.Core + Rask.Bootstrap …) is supplied by the host: the desktop unit
///     tests build it from real files, the browser host downloads the shipped <c>_framework/*.dll</c> and
///     wraps them via <c>MetadataReference.CreateFromStream</c> (no real filesystem in WASM). Each compile
///     uses a fresh assembly name — Mono WASM can't unload an <c>Assembly.Load</c>ed assembly, so repeated
///     runs accumulate; that's an accepted playground trade-off (reload the tab to reclaim). The live
///     as-you-type diagnostics/completion path (<see cref="PlaygroundWorkspace" />) deliberately never Emits
///     or loads, so typing does not leak assemblies — only pressing Run does.
/// </remarks>
public sealed class PlaygroundCompiler
{
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
        // Unique per compile: two assemblies sharing an identity would clash on Assembly.Load, and Mono
        // WASM never unloads, so a monotonic suffix keeps each run distinct.
        var assemblyName = "RaskPlayground_" + Interlocked.Increment(ref _counter);
        var compilation = PlaygroundCompilation.Create(source, _references, assemblyName, cancellationToken);

        // Bind once — on the single-threaded WASM interpreter a second GetDiagnostics() would re-bind the
        // whole compilation. Reused for both the display list and the error gate below.
        var compilationDiagnostics = compilation.Output.GetDiagnostics(cancellationToken);

        var diagnostics = new List<PlaygroundDiagnostic>();
        DiagnosticMapper.Collect(compilation.GeneratorDiagnostics, compilation.UserTree, diagnostics);
        DiagnosticMapper.Collect(compilationDiagnostics, compilation.UserTree, diagnostics);

        // Display-only lint pass. Best-effort: a throwing analyzer just means no RASK squiggles this run.
        await DiagnosticMapper
            .AppendAnalyzerDiagnosticsAsync(compilation.Output, compilation.UserTree, diagnostics, cancellationToken)
            .ConfigureAwait(false);

        // Only CS compile errors (or a failed Emit) block execution — Rask's RASK0## hints are advisory.
        if (compilationDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new PlaygroundResult(null, diagnostics, Succeeded: false);
        }

        using var peStream = new MemoryStream();
        var emit = compilation.Output.Emit(peStream, cancellationToken: cancellationToken);
        if (!emit.Success)
        {
            foreach (var d in emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(DiagnosticMapper.Map(d));
            }

            return new PlaygroundResult(null, diagnostics, false);
        }

        var assembly = Assembly.Load(peStream.ToArray());
        var entry = FindEntryComponent(assembly, out var ambiguous);
        if (entry is null)
        {
            diagnostics.Add(DiagnosticMapper.Synthetic(ambiguous
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
            diagnostics.Add(DiagnosticMapper.Synthetic($"Constructing '{entry.Name}' threw: {root.Message}"));
            return new PlaygroundResult(null, diagnostics, false);
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
}
