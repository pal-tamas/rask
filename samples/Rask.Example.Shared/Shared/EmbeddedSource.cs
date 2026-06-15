using System.Collections.Concurrent;
using System.Reflection;

namespace Rask.Example.Shared;

// Reads the verbatim text of a demo source file that the build embeds as a manifest
// resource (see the EmbeddedResource glob in Rask.Example.Shared.csproj, logical name
// "raksrc/{file}"). CodeSample uses this to show the *real* source beside the live
// Result, so the snippet always compiles and never drifts from what actually runs.
public static class EmbeddedSource
{
    private static readonly Assembly Asm = typeof(EmbeddedSource).Assembly;

    // Reading a manifest stream allocates; the set of demo files is small and fixed, so
    // memoise per file name (mirrors CodeSample's HighlightCache rationale).
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    // Reads one embedded file's verbatim text. The file name is the bare leaf name, e.g.
    // "ElementRefDemo.cs" (CodeSample shows one file per tab, so there is no joining).
    public static string Read(string fileName) =>
        Cache.GetOrAdd(fileName, static name =>
        {
            using var stream = Asm.GetManifestResourceStream($"raksrc/{name}")
                ?? throw new InvalidOperationException(
                    $"Embedded source 'raksrc/{name}' was not found. Ensure {name} lives under " +
                    "samples/Rask.Example.Shared/Features or Shared and matches the EmbeddedResource glob.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().TrimEnd();
        });
}
