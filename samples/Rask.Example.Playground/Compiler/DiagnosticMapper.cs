using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators;

namespace Rask.Example.Playground.Compiler;

/// <summary>
///     Turns Roslyn <see cref="Diagnostic" />s into the <see cref="PlaygroundDiagnostic" />s the editor
///     shows, and runs Rask's own analyzers (RASK0##) as a display-only lint pass — shared by the Run
///     pipeline (<see cref="PlaygroundCompiler" />) and the live analysis pipeline
///     (<see cref="PlaygroundWorkspace" />) so both surface identical messages, positions and filtering.
///     Roslyn line/columns are 0-based; Monaco markers are 1-based, converted here at the single boundary.
/// </summary>
internal static class DiagnosticMapper
{
    // Rask's analyzers live in the generator assembly; instantiate every concrete DiagnosticAnalyzer once
    // and reuse across compiles. Purely for surfacing RASK0## hints — they never affect what runs.
    private static readonly ImmutableArray<DiagnosticAnalyzer> RaskAnalyzers = DiscoverAnalyzers();
    /// <summary>
    ///     Adds every visible diagnostic that belongs to the user's own source to <paramref name="into" />.
    ///     Hidden diagnostics are dropped, and a diagnostic pointing into a generated tree (not
    ///     <paramref name="userTree" />) is skipped — it would reference lines the visitor never wrote.
    ///     Location-less (global) diagnostics are still shown.
    /// </summary>
    public static void Collect(
        IEnumerable<Diagnostic> source, SyntaxTree userTree, List<PlaygroundDiagnostic> into)
    {
        foreach (var d in source)
        {
            if (d.Severity == DiagnosticSeverity.Hidden)
            {
                continue;
            }

            if (d.Location.SourceTree is { } st && st != userTree)
            {
                continue;
            }

            into.Add(Map(d));
        }
    }

    /// <summary>Maps a single Roslyn diagnostic to the editor's 1-based <see cref="PlaygroundDiagnostic" />.</summary>
    public static PlaygroundDiagnostic Map(Diagnostic d)
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

    /// <summary>A playground-authored error (no Roslyn origin), anchored at the top of the file.</summary>
    public static PlaygroundDiagnostic Synthetic(string message) =>
        new("RASKPLAY", PlaygroundSeverity.Error, message, 1, 1, 1, 1);

    /// <summary>
    ///     Display-only lint pass: run Rask's analyzers over <paramref name="output" /> and add their hints
    ///     for the user's tree. Best-effort — an analyzer that throws on the bespoke playground setup must
    ///     not sink a valid compile, so failures just mean no RASK squiggles this run.
    /// </summary>
    public static async Task AppendAnalyzerDiagnosticsAsync(
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
            Collect(analyzerDiagnostics, userTree, into);
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
}
