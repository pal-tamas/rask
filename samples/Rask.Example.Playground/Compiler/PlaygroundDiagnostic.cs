namespace Rask.Example.Playground.Compiler;

/// <summary>Severity of a <see cref="PlaygroundDiagnostic" />, mapped from Roslyn's <c>DiagnosticSeverity</c>.</summary>
public enum PlaygroundSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
///     A single compiler / analyzer message surfaced from an in-browser compile — a Roslyn CS#### or a
///     Rask RASK0## diagnostic. Line/column are 1-based (Monaco's convention) so they map straight onto
///     editor markers; Roslyn's <c>LinePosition</c> is 0-based and is converted at the boundary.
/// </summary>
public sealed record PlaygroundDiagnostic(
    string Id,
    PlaygroundSeverity Severity,
    string Message,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);
