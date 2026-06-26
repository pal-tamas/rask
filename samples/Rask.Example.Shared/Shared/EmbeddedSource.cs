using System.Collections.Concurrent;
using System.Reflection;

namespace Rask.Example.Shared;

// Reads the verbatim text of a demo source file that the build embeds as a manifest
// resource (see the EmbeddedResource glob in Rask.Example.Shared.csproj, logical name
// "raksrc/{file}"). CodeSample uses this to show the *real* source beside the live
// Result, so the snippet always compiles and never drifts from what actually runs.
public static class EmbeddedSource
{
    // Assemblies whose Features/Shared sources are embedded as "raksrc/{leaf}" resources.
    // Seeded with this (Shared) assembly; a WASM host registers its own app assembly via
    // RegisterAssembly so its WASM-only demos (e.g. PwaDemo) can show their source too —
    // those live in the app assembly, not Rask.Example.Shared. Copy-on-write so the
    // render-time reads below stay lock-free; the rare registration takes the gate.
    private static volatile Assembly[] _sources = [typeof(EmbeddedSource).Assembly];
    private static readonly object Gate = new();

    // Reading a manifest stream allocates; the set of demo files is small and fixed, so
    // memoise per file name (mirrors CodeSample's HighlightCache rationale).
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    // Registers an additional assembly to resolve embedded demo sources from. Idempotent;
    // call once at startup (before any CodeSample renders). Used by WASM hosts that keep
    // their WASM-only demos in the app assembly rather than in Rask.Example.Shared.
    public static void RegisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (Gate)
        {
            if (Array.IndexOf(_sources, assembly) < 0)
            {
                _sources = [.. _sources, assembly];
            }
        }
    }

    // Reads one embedded file's verbatim text. The file name is the bare leaf name, e.g.
    // "ElementRefDemo.cs" (CodeSample shows one file per tab, so there is no joining). The
    // registered assemblies are searched in order; leaf names are unique within each.
    public static string Read(string fileName) =>
        Cache.GetOrAdd(fileName, static name =>
        {
            foreach (var asm in _sources)
            {
                var stream = asm.GetManifestResourceStream($"raksrc/{name}");
                if (stream is null)
                {
                    continue;
                }

                using (stream)
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd().TrimEnd();
                }
            }

            throw new InvalidOperationException(
                $"Embedded source 'raksrc/{name}' was not found in any registered assembly " +
                $"({string.Join(", ", _sources.Select(a => a.GetName().Name))}). Ensure {name} lives " +
                "under a Features or Shared folder that matches the EmbeddedResource glob, and that a " +
                "WASM-only demo's app assembly is registered via EmbeddedSource.RegisterAssembly.");
        });
}
