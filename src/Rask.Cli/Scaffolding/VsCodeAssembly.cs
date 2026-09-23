using System.Text;

namespace Rask.Cli.Scaffolding;

/// <summary>Which VS Code debugging setup a template ships.</summary>
internal enum VsCodeSetup
{
    /// <summary>No <c>.vscode/</c> at all.</summary>
    None,

    /// <summary>An ASP.NET host: F5 runs it under the C# debugger.</summary>
    Host,

    /// <summary>
    ///     An ASP.NET host with a Rask WebAssembly client (the <c>wasm-hosted</c> template): the host under the C# debugger,
    ///     then the client in a browser under the JavaScript debugger, through the host's debug proxy.
    /// </summary>
    WasmHost,

    /// <summary>A standalone browser-WASM app: its dev server in the background, the app in a debugged browser.</summary>
    WasmBrowser,
}

/// <summary>
///     Adds the VS Code debugging setup to a template's files: F5 builds the app as a dev session and runs it
///     under the debugger that can reach its code.
/// </summary>
/// <remarks>
///     <para>
///         Committed fragments (<c>src/Rask.Templates/_vscode*/</c>) rather than a copy in each template, because
///         the files are the same for every template of a kind — the only thing that differs is the project's
///         name, which is the placeholder every template already uses. Assembled the way
///         <see cref="IslandAssembly" /> assembles islands. A later fragment replaces a file of the same path from
///         an earlier one, so <see cref="VsCodeSetup.WasmHost" /> is the host setup with one file swapped.
///     </para>
///     <para>
///         Why F5 rather than attaching to <c>rask dev</c>: the runtime refuses to apply a hot-reload update
///         while a debugger is attached, so <c>dotnet watch</c> and a debugger cannot share a process. Under
///         F5 the editor's debugger launches the app — edits need a restart there, and <c>rask dev</c> stays
///         the live-edit loop — and the build's <c>RaskDevSession=true</c> is what tells the app to start its
///         own front-end dev servers. (C# Dev Kit's debug hot reload was tried for this launch and reported
///         unavailable, so nothing here promises it.)
///     </para>
///     <para>
///         C# that runs in the browser cannot be reached by the coreclr debugger. It is debugged by launching a
///         browser under VS Code's JavaScript debugger with an <c>inspectUri</c> through the WebAssembly debug
///         proxy: the SDK's dev server maps one for a standalone app, and <c>UseRaskSpa</c> maps one in
///         Development for a host serving its client (#1073).
///     </para>
/// </remarks>
internal static class VsCodeAssembly
{
    /// <summary>The stylesheet a Tailwind template compiles, relative to the project.</summary>
    internal const string TailwindEntry = "Styles/app.css";

    /// <summary>
    ///     The fragment roots each setup is assembled from, in order; a later one wins a path. Every setup ends
    ///     with the editor settings (<c>_vscode-editor</c>), which <c>_vscode-tailwind</c> replaces — together
    ///     with the extension recommendations — for a template that compiles Tailwind.
    /// </summary>
    internal static IReadOnlyList<string> FragmentRoots(VsCodeSetup setup, bool tailwind)
    {
        IReadOnlyList<string> debug = setup switch
        {
            VsCodeSetup.None => [],
            VsCodeSetup.Host => ["_vscode"],
            VsCodeSetup.WasmHost => ["_vscode", "_vscode-wasmhost"],
            VsCodeSetup.WasmBrowser => ["_vscode-wasm"],
            _ => throw new ArgumentOutOfRangeException(nameof(setup), setup, null),
        };

        return debug.Count == 0 ? [] : [.. debug, "_vscode-editor", .. tailwind ? ["_vscode-tailwind"] : Array.Empty<string>()];
    }

    /// <summary>
    ///     Whether <paramref name="files" /> compile Tailwind: they carry <see cref="TailwindEntry" /> importing it.
    ///     Read from what the template actually wrote, so a template that gains or drops Tailwind needs no list here.
    /// </summary>
    internal static bool CompilesTailwind(string targetDirectory, IReadOnlyList<ScaffoldFile> files) =>
        files.Any(f => Path.GetRelativePath(targetDirectory, f.Path).Replace('\\', '/') == TailwindEntry
                       && f.Content.Contains("@import \"tailwindcss\"", StringComparison.Ordinal));

    /// <summary>
    ///     <paramref name="existing" /> plus the <c>.vscode/</c> files for <paramref name="setup" />, named for
    ///     <paramref name="name" />.
    /// </summary>
    public static IReadOnlyList<ScaffoldFile> Apply(
        string targetDirectory,
        string name,
        IReadOnlyList<ScaffoldFile> existing,
        VsCodeSetup setup)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetDirectory);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(existing);

        var fragment = new Dictionary<string, ScaffoldFile>(StringComparer.Ordinal);
        foreach (var root in FragmentRoots(setup, CompilesTailwind(targetDirectory, existing)))
        {
            foreach (var asset in TemplateAssets.Load(root))
            {
                fragment[asset.Path] = new ScaffoldFile(
                    Path.Combine(targetDirectory, asset.Path.Replace('/', Path.DirectorySeparatorChar)),
                    Encoding.UTF8.GetString(asset.Bytes)
                        .Replace(TemplateMaterializer.NameToken, name, StringComparison.Ordinal));
            }
        }

        return [.. existing, .. fragment.Values];
    }
}
