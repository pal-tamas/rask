using Rask.Core;

namespace Rask.Example.Playground.Compiler;

/// <summary>
///     Outcome of <see cref="PlaygroundCompiler.CompileAsync" />: the instantiated entry component
///     (null when the compile failed or produced no component), every surfaced diagnostic, and a
///     success flag. A successful compile can still carry warning/info diagnostics — Rask's own lint
///     hints (RASK0##) are shown but never block execution; only CS compile errors do.
/// </summary>
public sealed record PlaygroundResult(
    Component? Component,
    IReadOnlyList<PlaygroundDiagnostic> Diagnostics,
    bool Succeeded);
