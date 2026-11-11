namespace Rask.TestFiles;

/// <summary>
///     The WebAssembly debugger's files, looked for in a publish output, where none may be.
/// </summary>
/// <remarks>
///     The debug proxy (<c>BrowserDebugHost</c>, <c>BrowserDebugProxy</c>, beside the SDK's dev server <c>WasmAppHost</c>)
///     turns any DevTools socket it is pointed at into a debugger of the app, so it is a development tool and nothing
///     else: CVE-2026-102870 was in it. Rask reaches it only through the path its Debug build writes to
///     <c>{name}.rask-debugproxy</c> beside the dev manifest, which <c>MapRaskSpa</c> reads in Development — so a
///     publish that carried any of these, or that path file, or the dev manifest it sits beside, would be shipping the
///     debugger, or the way to find one, to production.
/// </remarks>
internal static class DebuggerFreePublish
{
    private static readonly string[] Patterns =
    [
        "BrowserDebugHost.*",
        "BrowserDebugProxy.*",
        "WasmAppHost.*",
        "Microsoft.AspNetCore.Components.WebAssembly.DevServer.*",
        "*.rask-debugproxy",
        "*.staticwebassets.runtime.json",
    ];

    /// <summary>Every debugger file under <paramref name="publishDirectory" />, relative to it; empty for a clean publish.</summary>
    public static IReadOnlyList<string> DebuggerFilesIn(string publishDirectory) =>
        Patterns
            .SelectMany(pattern => Directory.EnumerateFiles(publishDirectory, pattern, SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(publishDirectory, path))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
}
