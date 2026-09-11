using System.Text;

namespace Rask.Cli.Scaffolding;

/// <summary>
///     Adds the VS Code debugging setup to a host template's files: F5 builds the app as a dev session and
///     runs it under the C# debugger.
/// </summary>
/// <remarks>
///     <para>
///         One committed fragment (<c>src/Rask.Templates/_vscode/</c>) rather than a copy in each host
///         template, because the files are the same for every one of them — the only thing that differs is
///         the project's name, which is the placeholder every template already uses. Assembled the way
///         <see cref="IslandAssembly" /> assembles islands.
///     </para>
///     <para>
///         Why F5 rather than attaching to <c>rask dev</c>: the runtime refuses to apply a hot-reload update
///         while a debugger is attached, so <c>dotnet watch</c> and a debugger cannot share a process. Under
///         F5 the editor's debugger launches the app and applies edits itself, and the build's
///         <c>RaskDevSession=true</c> is what tells the app to start its own front-end dev servers.
///     </para>
///     <para>
///         Not for the <c>wasm</c> template. Its C# runs in the browser, which needs a debug proxy and a
///         browser launched for debugging rather than the coreclr debugger.
///     </para>
/// </remarks>
internal static class VsCodeAssembly
{
    /// <summary>The fragment root inside the embedded template payload.</summary>
    internal const string FragmentRoot = "_vscode";

    /// <summary>
    ///     <paramref name="existing" /> plus the <c>.vscode/</c> files, named for <paramref name="name" />.
    /// </summary>
    public static IReadOnlyList<ScaffoldFile> Apply(
        string targetDirectory,
        string name,
        IReadOnlyList<ScaffoldFile> existing)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetDirectory);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(existing);

        var assets = TemplateAssets.Load(FragmentRoot);
        var files = new List<ScaffoldFile>(existing.Count + assets.Count);
        files.AddRange(existing);

        foreach (var asset in assets)
        {
            files.Add(new ScaffoldFile(
                Path.Combine(targetDirectory, asset.Path.Replace('/', Path.DirectorySeparatorChar)),
                Encoding.UTF8.GetString(asset.Bytes)
                    .Replace(TemplateMaterializer.NameToken, name, StringComparison.Ordinal)));
        }

        return files;
    }
}
