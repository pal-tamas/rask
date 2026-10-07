using System.Text;

namespace Rask.Cli.Scaffolding;

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
///         proxy: the SDK's dev server maps one for a standalone app, and <c>MapRaskSpa</c> maps one in
///         Development for a host serving its client (#1073).
///     </para>
/// </remarks>
internal static class VsCodeAssembly
{
    /// <summary>The stylesheet a Tailwind template compiles, relative to the project.</summary>
    internal const string TailwindEntry = "Styles/app.css";

    /// <summary>
    ///     The fragment roots each setup is assembled from, in order; a later one wins a path. Every setup ends
    ///     with the editor settings (<c>_vscode-editor</c>), which <c>_vscode-tailwind</c> replaces for a template
    ///     that compiles Tailwind. The extension recommendations are no fragment: see <see cref="Recommendations" />.
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

        if (debug.Count == 0)
        {
            return [];
        }

        string[] editor = tailwind ? ["_vscode-editor", "_vscode-tailwind"] : ["_vscode-editor"];
        return [.. debug, .. editor];
    }

    /// <summary>
    ///     Whether <paramref name="files" /> compile Tailwind: they carry <see cref="TailwindEntry" /> importing it —
    ///     itself, or through the UI kit's <c>rask-ui.css</c>, which brings Tailwind and the kit in one line.
    ///     Read from what the template actually wrote, so a template that gains or drops Tailwind needs no list here.
    /// </summary>
    internal static bool CompilesTailwind(string targetDirectory, IReadOnlyList<ScaffoldFile> files) =>
        files.Any(f => string.Equals(Path.GetRelativePath(targetDirectory, f.Path).Replace('\\', '/'), TailwindEntry, StringComparison.Ordinal)
                       && ImportsTailwind(f.Content));

    internal static bool ImportsTailwind(string stylesheet) =>
        stylesheet.Contains("@import \"tailwindcss\"", StringComparison.Ordinal)
        || stylesheet.Contains("@import \"./vendor/rask-ui.css\"", StringComparison.Ordinal);

    /// <summary>
    ///     What <c>.vscode/extensions.json</c> recommends, why, and whether this project needs it — read from the
    ///     files the scaffold wrote, so vue islands on a server app get Volar as the vue template does.
    /// </summary>
    /// <remarks>
    ///     Generated rather than a fragment: a fragment replaces the whole file, and Tailwind × islands is a
    ///     product no set of committed files keeps in step.
    /// </remarks>
    private static readonly (string Id, string Why, Func<string, IReadOnlyList<ScaffoldFile>, bool> Wanted)[] Recommendations =
    [
        ("ms-dotnettools.csdevkit",
            "C# Dev Kit: C# editing, and the C# debugger F5 runs a host under. C# in the browser needs nothing more —\n"
            + "    // VS Code's built-in JavaScript debugger attaches through the WebAssembly debug proxy.",
            static (_, _) => true),
        ("editorconfig.editorconfig",
            "EditorConfig: .editorconfig for the files C# Dev Kit does not format — TypeScript, CSS, JSON.",
            static (_, _) => true),
        ("usernamehw.errorlens",
            "Error Lens: diagnostics inline on their line — the build treats every analyzer warning as an error.",
            static (_, _) => true),
        ("bradlc.vscode-tailwindcss",
            "Tailwind CSS IntelliSense: class completion inside Div.Class(\"…\"), set up in settings.json.",
            CompilesTailwind),
        ("dbaeumer.vscode-eslint", "ESLint: the front end's eslint.config.mjs, as you type.",
            static (_, files) => Any(files, name => name.StartsWith("eslint.config.", StringComparison.Ordinal))),
        ("esbenp.prettier-vscode", "Prettier: the front end's .prettierrc, on format.",
            static (_, files) => Any(files, name => string.Equals(name, ".prettierrc", StringComparison.Ordinal))),
        ("vue.volar", "Vue (Official): .vue single-file components.",
            static (_, files) => Any(files, name => name.EndsWith(".vue", StringComparison.Ordinal))),
        ("svelte.svelte-vscode", "Svelte: .svelte components.",
            static (_, files) => Any(files, name => name.EndsWith(".svelte", StringComparison.Ordinal))),
        ("angular.ng-template", "Angular Language Service: completion and checking inside Angular templates.",
            static (_, files) => Any(files, name => string.Equals(name, "angular.json", StringComparison.Ordinal))),
    ];

    private static bool Any(IReadOnlyList<ScaffoldFile> files, Func<string, bool> name) =>
        files.Any(f => name(Path.GetFileName(f.Path)));

    private static ScaffoldFile ExtensionsJson(string targetDirectory, IReadOnlyList<ScaffoldFile> files)
    {
        var wanted = Recommendations.Where(r => r.Wanted(targetDirectory, files)).ToList();
        var json = new StringBuilder()
            .Append("{\n  // VS Code offers to install these when the folder opens; each is here for something this project holds.\n")
            .Append("  \"recommendations\": [\n");

        for (var i = 0; i < wanted.Count; i++)
        {
            json.Append("    // ").Append(wanted[i].Why).Append('\n')
                .Append("    \"").Append(wanted[i].Id).Append(i < wanted.Count - 1 ? "\",\n" : "\"\n");
        }

        json.Append("  ]\n}\n");
        return new ScaffoldFile(Path.Combine(targetDirectory, ".vscode", "extensions.json"), json.ToString());
    }

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

        return fragment.Count == 0 ? existing : [.. existing, .. fragment.Values, ExtensionsJson(targetDirectory, existing)];
    }
}
