using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Tests;

/// <summary>Runs one analyzer over a compilation that must compile first.</summary>
/// <remarks>
///     A file with a compile error reports no analyzer diagnostics at all, and would pass every "is not reported"
///     case for the wrong reason — so a source that stops compiling fails the test instead.
/// </remarks>
internal static class CompiledAnalysis
{
    internal static async Task<ImmutableArray<Diagnostic>> RunAsync(Compilation compilation, DiagnosticAnalyzer analyzer)
    {
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
        {
            throw new Xunit.Sdk.XunitException(
                "The test source does not compile, so no analyzer result means anything:\n  "
                + string.Join("\n  ", errors.Select(e => e.ToString())));
        }

        return await compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync();
    }
}
