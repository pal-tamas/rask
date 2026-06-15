using System.Collections.Concurrent;
using System.Reflection;

namespace Rask.Example.Shared.Demos;

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

    // Reads one or more embedded files and joins them with a blank line, so a single tab
    // can show a pair of sibling files (e.g. ScopedRed.cs + ScopedBlue.cs). File names are
    // the bare leaf names, e.g. "ElementRefDemo.cs".
    public static string Read(params string[] fileNames)
    {
        if (fileNames.Length == 1)
        {
            return ReadOne(fileNames[0]);
        }

        return string.Join("\n\n", fileNames.Select(ReadOne));
    }

    private static string ReadOne(string fileName) =>
        Cache.GetOrAdd(fileName, static name =>
        {
            using var stream = Asm.GetManifestResourceStream($"raksrc/{name}")
                ?? throw new InvalidOperationException(
                    $"Embedded source 'raksrc/{name}' was not found. Ensure {name} lives under " +
                    "samples/Rask.Example.Shared/Demos and matches the EmbeddedResource glob.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().TrimEnd();
        });
}
