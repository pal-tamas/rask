using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Build.Framework;
using Rask.Spa.Tasks;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.External.Tasks;

/// <summary>
///     Writes each external component's generated prop types into <c>obj/</c>, and the tsconfig
///     fragment that gives them an import path.
/// </summary>
/// <remarks>
///     <para>
///         This is the half that makes the contract two-way. Without it the generator's interfaces
///         exist only as constants inside the assembly, and the <c>.tsx</c> goes on typing its props by
///         hand — so renaming a C# property breaks the front end silently, at runtime, in the browser.
///     </para>
///     <para>
///         Into <c>obj/</c> rather than beside the source, because these are build output: a generated
///         file in the source tree is one someone edits by hand and loses on the next build, and one
///         that shows up in every review diff.
///     </para>
///     <para>
///         Runs between the C# compile and the bundler: the constants only exist once Roslyn has
///         produced the assembly, and the files are inputs to whatever compiles the front end.
///     </para>
/// </remarks>
public sealed class WriteExternalPropTypesTask : Task
{
    private const string GeneratedNamespace = "Rask.External.Generated";
    private const string GeneratedTypeName = "RaskExternalGeneratedTypeScript";

    /// <summary>The just-compiled assembly to read the constants out of.</summary>
    [Required]
    public string AssemblyPath { get; set; } = string.Empty;

    /// <summary>Where the <c>.d.ts</c> files go — under <c>obj/</c>, never the source tree.</summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Where the tsconfig fragment goes, for the app's tsconfig to <c>extends</c>.</summary>
    [Required]
    public string TsConfigPath { get; set; } = string.Empty;

    /// <summary>The project directory, which the emitted paths are written relative to.</summary>
    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    /// <summary>The front-end files to type-check, if any. Empty writes no check config.</summary>
    public ITaskItem[] FrontEndFiles { get; set; } = [];

    /// <summary>Where the type-check's own tsconfig goes.</summary>
    public string CheckConfigPath { get; set; } = string.Empty;

    /// <summary>Where the Vue checker's tsconfig goes. <c>vue-tsc</c> reads it; tsgo cannot.</summary>
    public string VueConfigPath { get; set; } = string.Empty;

    /// <summary>Where the Svelte checker's tsconfig goes. <c>svelte-check</c> reads it.</summary>
    public string SvelteConfigPath { get; set; } = string.Empty;

    /// <summary>Where the Solid islands' tsconfig goes. tsgo reads it, with Solid's JSX factory.</summary>
    public string SolidConfigPath { get; set; } = string.Empty;

    /// <summary>Where the Preact islands' tsconfig goes. tsgo reads it, with Preact's JSX factory.</summary>
    public string PreactConfigPath { get; set; } = string.Empty;

    /// <summary>Where the Angular islands' tsconfig goes. tsgo reads it, with decorators enabled.</summary>
    public string AngularConfigPath { get; set; } = string.Empty;

    /// <summary>The compiled assembly, which is what actually knows each island's runtime.</summary>
    public string IslandAssemblyPath { get; set; } = string.Empty;

    /// <summary>Whether the check config was written, so the caller knows there is one to run.</summary>
    [Output]
    public bool HasCheckConfig { get; private set; }

    /// <summary>Whether any Vue island was found, so the caller knows to run <c>vue-tsc</c>.</summary>
    [Output]
    public bool HasVueConfig { get; private set; }

    /// <summary>Whether any Solid island was found, so the caller knows to check it separately.</summary>
    [Output]
    public bool HasSolidConfig { get; private set; }

    /// <summary>Whether any Preact island was found, so the caller knows to check it separately.</summary>
    [Output]
    public bool HasPreactConfig { get; private set; }

    /// <summary>Whether any Angular island was found, so the caller knows to check it separately.</summary>
    [Output]
    public bool HasAngularConfig { get; private set; }

    /// <summary>Whether any Svelte island was found, so the caller knows to run <c>svelte-check</c>.</summary>
    [Output]
    public bool HasSvelteConfig { get; private set; }

    /// <inheritdoc />
    public override bool Execute()
    {
        // The tsconfig fragment FIRST, and before the assembly check, because it does not depend on the
        // assembly at all — it is a path mapping. The app's own tsconfig `extends` it, and Vite fails
        // hard on an extends that points at a missing file, so on a clean tree the bundle would
        // otherwise die before the compile that would have written this.
        Directory.CreateDirectory(OutputDirectory);
        var wroteConfig = WriteTsConfig();

        if (!File.Exists(AssemblyPath))
        {
            // Nothing to read yet — an early pass before the compile, or a build that already failed
            // for its own reasons. Either way a second error here would only bury the first.
            Log.LogMessage(
                MessageImportance.Low,
                $"Rask.External: no assembly at '{AssemblyPath}' yet — wrote the path mapping only.");
            return true;
        }

        try
        {
            var constants = GeneratedTypeScript.Read(AssemblyPath, GeneratedNamespace, GeneratedTypeName);

            // No early return when the assembly declares nothing (#943), but no check either — and the
            // difference between those two is the whole of this change.
            //
            // The original defect: returning here wrote no tsconfig.check.json, no tsconfig.vue.json and
            // no tsconfig.svelte.json, every checker in _RaskExternalTypeCheck is gated on its config
            // existing, so tsgo, vue-tsc and svelte-check ALL did nothing — while none of the three
            // "skipping…" messages fired, because each is gated on a different cause. Silent.
            //
            // The obvious repair, checking the discovered files anyway, was tried and is WRONG. The file
            // list cannot identify island code on its own, and two kinds of file proved it: Rask's own
            // scoped TypeScript, which is a plain .ts beside a component (#938), and the whole client/
            // tree of a meta-framework host — a front end that type-checks under ITS toolchain and not
            // under an island config. Checking them reported TS2304 on Next's generated globals and
            // TS2307 on @rask/client, in files that are perfectly correct.
            //
            // So the skip stays, and becomes loud and specific instead. Stale configs are deleted on the
            // way out, which the early return also skipped: a tsconfig.vue.json left by a previous build
            // is a checker that runs next time against a file list that may no longer exist.
            var declared = constants.Count > 0;
            var written = wroteConfig ? 1 : 0;

            foreach (var pair in constants)
            {
                var path = Path.Combine(OutputDirectory, pair.Key + ".props.d.ts");
                if (GeneratedTypeScript.WriteIfDifferent(path, pair.Value))
                {
                    written++;
                }
            }

            // Stale types are worse than missing ones: a deleted component would leave a .d.ts that
            // still type-checks, so the front end would keep compiling against something the server
            // no longer renders.
            Prune(constants.Keys);

            if (WriteCheckConfig(declared))
            {
                written++;
            }

            ReportUnbuiltIslands();

            if (!declared)
            {
                // Normal, not High. #943 is about SILENCE — a check that stops running and says nothing —
                // and Normal cures that: any `-v:n` build shows it, which is what someone asking "did my
                // islands get checked?" runs. High was tried and is its own version of the same defect:
                // seven projects in this repository legitimately have front-end files and no islands (the
                // six meta samples and the showcase's scoped TypeScript), so it printed a paragraph each,
                // on every build, about nothing being wrong. A line that always fires is a line nobody
                // reads, which is how the real occurrence would be missed.
                Log.LogMessage(
                    FrontEndFiles.Length > 0 ? MessageImportance.Normal : MessageImportance.Low,
                    $"Rask.External: '{Path.GetFileName(AssemblyPath)}' declares no external components, "
                    + $"so the prop type-check is skipped for the {FrontEndFiles.Length} front-end file(s) "
                    + "beside it. Without a declaration nothing distinguishes an island from scoped "
                    + "TypeScript or from a meta framework's own front end, and checking those reports "
                    + "errors that are not in your code. If you expected islands here, check that the "
                    + "component derives from ReactComponent/LitComponent/… and that "
                    + "RaskExternalPropTypes is not false.");
            }
            else if (written > 0)
            {
                Log.LogMessage(
                    MessageImportance.High,
                    $"Rask.External: wrote prop types for {constants.Count} component(s) to '{OutputDirectory}'.");
            }

            return true;
        }
        catch (Exception ex)
        {
            // Failing the build is right: the alternative is a front end compiling against the last
            // build's props, which type-checks and then arrives wrong in the browser.
            Log.LogError($"Rask.External: could not read the generated prop types from '{AssemblyPath}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Warns when a declared island's front-end file is not among the ones the build will bundle.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The generator decides what the rendered markup POINTS AT; the MSBuild globs decide what
    ///         actually gets BUILT. Nothing compared the two (#942), and the failure is silent and total:
    ///         the page renders, the chunk is never built, and the only symptom is
    ///         <c>Rask islands: 'Gauge' is not in the manifest</c> in the browser console — at run time,
    ///         in someone else's browser, with a green build behind it.
    ///     </para>
    ///     <para>
    ///         This task is the one place holding both halves: it reads the declared components out of
    ///         the compiled assembly and is handed <c>@(_RaskExternalFile)</c>. It had both and compared
    ///         neither.
    ///     </para>
    ///     <para>
    ///         A warning rather than an error, so a consumer mid-refactor is told rather than stopped —
    ///         <c>RaskExternalLitAutoPair=false</c> makes an unpaired island a SUPPORTED configuration
    ///         rather than a typo. Worth being plain about what that means HERE though: this repository
    ///         builds with <c>-warnaserror</c>, so in its own gate this does stop the build. That is the
    ///         right outcome for a repo whose samples are the documentation, and it is the consumer who
    ///         gets the softer treatment.
    ///     </para>
    /// </remarks>
    private void ReportUnbuiltIslands()
    {
        var modules = ExternalIslandMetadata.Modules(
            string.IsNullOrEmpty(IslandAssemblyPath) ? AssemblyPath : IslandAssemblyPath);

        if (modules.Count == 0)
        {
            return;
        }

        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in FrontEndFiles)
        {
            discovered.Add(Path.GetFileName(item.GetMetadata("FullPath").Replace('\\', '/')));
        }

        foreach (var pair in modules)
        {
            // Matched on the file NAME, which is the pairing rule everywhere else in this feature, and
            // the only one that survives a declared module written as "./Gauge.ts" against a discovered
            // item held as an absolute path.
            var file = Path.GetFileName(pair.Value.Replace('\\', '/'));

            if (file.Length == 0 || discovered.Contains(file))
            {
                continue;
            }

            Log.LogWarning(
                $"Rask.External: '{pair.Key}' declares the front-end file '{file}', which is not among "
                + "the files this project will bundle. Its markup will render and its chunk will not "
                + "exist, so the browser reports \"'" + pair.Key + "' is not in the manifest\" and the "
                + "island never mounts. Either put the file where the island globs reach it, or declare "
                + "it explicitly with a <RaskExternal Include=\"…\"/> item.");
        }
    }

    /// <summary>Deletes the <c>.d.ts</c> of a component that no longer exists.</summary>
    private void Prune(IEnumerable<string> current)
    {
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in current)
        {
            keep.Add(name + ".props.d.ts");
        }

        foreach (var file in Directory.GetFiles(OutputDirectory, "*.props.d.ts"))
        {
            if (keep.Contains(Path.GetFileName(file)))
            {
                continue;
            }

            File.Delete(file);
            Log.LogMessage(MessageImportance.Low, $"Rask.External: removed stale '{Path.GetFileName(file)}'.");
        }
    }

    /// <summary>
    ///     Writes the tsconfig fragment the app's own tsconfig extends.
    /// </summary>
    /// <remarks>
    ///     A fragment to `extends` rather than an edit to the app's tsconfig.json. Rewriting someone
    ///     else's config would have to preserve their comments, their key order and their formatting,
    ///     and would still surprise anyone who opened it — for a path mapping that is one line to add
    ///     once and obvious to remove.
    /// </remarks>
    private bool WriteTsConfig()
    {
        // Relative to the fragment's own directory, and forward-slashed: a Windows path inside JSON is
        // a string full of escape sequences, and tsconfig resolves relative to the file it is in.
        var relative = Relative(Path.GetDirectoryName(TsConfigPath) ?? ProjectDirectory, OutputDirectory);

        var json = new StringBuilder();
        json.AppendLine("{");
        json.AppendLine("  // <auto-generated/> Rask writes this; your tsconfig.json extends it.");
        json.AppendLine("  \"compilerOptions\": {");
        // No baseUrl: TypeScript 7 removed it (TS5102), and a paths entry has to be relative to the
        // config's own directory with an explicit './' or it is rejected as non-relative (TS5090).
        json.AppendLine("    \"paths\": {");
        json.Append("      \"@rask/*\": [\"").Append(Dotted(relative)).AppendLine("/*\"]");
        json.AppendLine("    }");
        json.AppendLine("  }");
        json.AppendLine("}");

        return GeneratedTypeScript.WriteIfDifferent(TsConfigPath, json.ToString());
    }

    /// <summary>
    ///     Writes the tsconfig the type-check runs against.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Its own config rather than the app's, and not optional: the <c>@rask/*</c> alias only
    ///         resolves when tsgo reads a tsconfig, so checking loose files on a command line would
    ///         report the import as missing and never reach the props at all — a check that fails for
    ///         the wrong reason, which is worse than no check.
    ///     </para>
    ///     <para>
    ///         Separate from the app's config so the check does not inherit settings that would change
    ///         what it means — a looser <c>strict</c>, a different <c>jsx</c>, an <c>exclude</c> that
    ///         happens to cover a component — and so running it can never depend on the app having
    ///         written a tsconfig at all.
    ///     </para>
    /// </remarks>
    /// <param name="declared">
    ///     Whether the assembly declared any island at all. When it did not, every checker's file list is
    ///     empty, which DELETES a stale config rather than leaving it to run next build — see the note in
    ///     <see cref="Execute" /> for why checking the discovered files instead is wrong.
    /// </param>
    private bool WriteCheckConfig(bool declared)
    {
        if (CheckConfigPath.Length == 0)
        {
            HasCheckConfig = false;
            return false;
        }

        var directory = Path.GetDirectoryName(CheckConfigPath) ?? ProjectDirectory;
        var written = false;

        // Split by what can actually READ the file — tsgo parses TypeScript and JSX and nothing else,
        // so a single .vue reaching its "files" array is not a weaker check but a build that stops
        // working — and then, WITHIN what tsgo can read, by what the file needs to be read CORRECTLY.
        //
        // That second split is what Solid and Angular added. One "jsx" setting cannot serve three JSX
        // runtimes: Solid's `class=` attribute is an error under React's JSX types, and Preact's hooks
        // resolve to React's without its own jsxImportSource. Angular is worse than wrong — its
        // decorators need experimentalDecorators, which Lit 3's standard decorators need OFF.
        //
        // A config that fails for the wrong reason is worse than no config: the targets run a checker
        // whenever its config exists, so a Solid island in the shared config would break the build of
        // a project whose TypeScript is perfectly fine.
        var runtimes = Runtimes();

        HasSolidConfig = WriteConfigFor(
            SolidConfigPath, directory, p => declared && Is(runtimes, p, "solid"), ref written,
            // "preserve" hands the JSX to the Solid plugin rather than compiling it here, and
            // jsxImportSource is what points the TYPES at solid-js instead of React.
            ["\"jsx\": \"preserve\"", "\"jsxImportSource\": \"solid-js\""]);

        HasPreactConfig = WriteConfigFor(
            PreactConfigPath, directory, p => declared && Is(runtimes, p, "preact"), ref written,
            ["\"jsx\": \"react-jsx\"", "\"jsxImportSource\": \"preact\""]);

        HasAngularConfig = WriteConfigFor(
            AngularConfigPath, directory, p => declared && Is(runtimes, p, "angular"), ref written,
            // Angular's decorators are the TypeScript 4 form. Lit 3's are the standard ones and need
            // this OFF, which is exactly why the two cannot share a config.
            ["\"experimentalDecorators\": true", "\"emitDecoratorMetadata\": true"]);

        // Everything else tsgo can read: React, Lit, and any JSX file no component has claimed yet.
        //
        // A bare .ts or .js is the exception, and it has to be: `Name.ts` is claimed by filename for a
        // Lit or Angular island AND by Rask.Core for scoped TypeScript, which is #938. Nothing in the
        // extension separates them — but this task runs AFTER the compile, so the assembly can. A .ts
        // that no declared island claims is scoped TypeScript, and checking it here fails for a reason
        // that has nothing to do with the author's code: it is compiled by a different pipeline, with
        // different lib and ambient declarations, so its globals resolve to nothing. Rask's own showcase
        // has one (Features/Gantt/Gantt.ts, whose types come from a third-party global), and it reported
        // four TS2304 on names that are perfectly well defined where that file is actually compiled.
        //
        // .tsx, .jsx, .vue and .svelte need no such test: scoped TypeScript is never any of them.
        //
        // The narrowing costs one case — a real Lit island in a project whose generator is suppressed is
        // no longer checked — and that case could not be checked correctly anyway, for the same reason.
        HasCheckConfig = WriteConfigFor(
            CheckConfigPath,
            directory,
            p => declared && IsTypeScript(p) && !Is(runtimes, p, "solid") && !Is(runtimes, p, "preact")
                 && !Is(runtimes, p, "angular")
                 && (!IsAmbiguouslyPaired(p) || ExternalIslandMetadata.RuntimeFor(runtimes, p) is not null),
            ref written,
            ReactJsx);

        // Vue and Svelte keep the JSX setting every config used to carry unconditionally. Their own
        // checkers read it: a .vue with `lang="tsx"` or a JSX render function, or a .svelte whose
        // program pulls in a TSX helper, would otherwise be checked under TypeScript's default `jsx`
        // and fail on code that checked cleanly before.
        HasVueConfig = WriteConfigFor(
            VueConfigPath, directory, p => declared && HasExtension(p, ".vue"), ref written, ReactJsx);
        HasSvelteConfig = WriteConfigFor(
            SvelteConfigPath, directory, p => declared && HasExtension(p, ".svelte"), ref written, ReactJsx);

        return written;
    }

    /// <summary>Writes one checker's config over the files it can read, or removes a stale one.</summary>
    /// <param name="configPath">Where the config goes. Empty skips it.</param>
    /// <param name="directory">The config's own directory, which its relative paths are resolved from.</param>
    /// <param name="matches">Which front-end files belong to this checker.</param>
    /// <param name="written">Raised when anything was written or deleted.</param>
    /// <param name="options">
    ///     Compiler options this checker needs and the others must not have — the JSX factory, or
    ///     Angular's decorators. Written as raw JSON lines, already quoted.
    /// </param>
    private bool WriteConfigFor(
        string configPath,
        string directory,
        Func<string, bool> matches,
        ref bool written,
        IReadOnlyList<string>? options = null)
    {
        if (configPath.Length == 0)
        {
            return false;
        }

        var files = new List<string>();
        foreach (var item in FrontEndFiles)
        {
            var full = item.GetMetadata("FullPath");
            if (matches(full))
            {
                files.Add(Relative(directory, full));
            }
        }

        if (files.Count == 0)
        {
            // Deleted rather than left behind. A config listing files that are gone would have the
            // checker fail on a project whose last Vue island was removed — and the targets decide
            // whether to run a checker by whether its config exists.
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
                written = true;
            }

            return false;
        }

        files.Sort(StringComparer.Ordinal);

        var json = new StringBuilder();
        json.AppendLine("{");
        json.AppendLine("  // <auto-generated/> Rask writes this to type-check components against their");
        json.AppendLine("  // generated props. Not the app's tsconfig, and not meant to be edited.");
        json.AppendLine("  \"compilerOptions\": {");
        json.AppendLine("    \"noEmit\": true,");
        json.AppendLine("    \"strict\": true,");
        json.AppendLine("    \"skipLibCheck\": true,");
        json.AppendLine("    \"target\": \"esnext\",");
        json.AppendLine("    \"module\": \"preserve\",");
        json.AppendLine("    \"moduleResolution\": \"bundler\",");
        json.AppendLine("    \"lib\": [\"esnext\", \"dom\"],");

        foreach (var option in options ?? [])
        {
            json.Append("    ").Append(option).AppendLine(",");
        }

        json.AppendLine("    \"paths\": {");
        json.Append("      \"@rask/*\": [\"").Append(Dotted(Relative(directory, OutputDirectory)))
            .AppendLine("/*\"]");
        json.AppendLine("    }");
        json.AppendLine("  },");
        json.AppendLine("  \"files\": [");

        for (var i = 0; i < files.Count; i++)
        {
            json.Append("    \"").Append(files[i]).Append('"')
                .AppendLine(i == files.Count - 1 ? string.Empty : ",");
        }

        json.AppendLine("  ]");
        json.AppendLine("}");

        if (GeneratedTypeScript.WriteIfDifferent(configPath, json.ToString()))
        {
            written = true;
        }

        return true;
    }

    /// <summary>The JSX setting every checker but Solid's and Angular's uses.</summary>
    private static readonly string[] ReactJsx = ["\"jsx\": \"react-jsx\""];

    /// <summary>Whether a front-end file's C# class declared the given runtime.</summary>
    private static bool Is(IReadOnlyDictionary<string, string> runtimes, string path, string runtime) =>
        string.Equals(ExternalIslandMetadata.RuntimeFor(runtimes, path), runtime, StringComparison.Ordinal);

    /// <summary>Each island's declared runtime, or an empty map when the assembly cannot be read.</summary>
    private Dictionary<string, string> Runtimes()
    {
        try
        {
            // IslandAssemblyPath rather than AssemblyPath: the same file, but the caller may not have
            // passed it, and falling back to the props assembly keeps this working either way.
            return ExternalIslandMetadata.Runtimes(
                string.IsNullOrEmpty(IslandAssemblyPath) ? AssemblyPath : IslandAssemblyPath);
        }
        catch (Exception ex)
        {
            Log.LogWarning(
                $"Rask.External: could not read the declared runtimes ({ex.Message}). Solid, Preact and "
                + "Angular islands will be type-checked with React's JSX settings, which will report "
                + "errors that are not in your code.");

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Whether tsgo can read this file at all.</summary>
    /// <summary>
    ///     Whether an extension is claimed by both an island and Rask.Core's scoped TypeScript, so the
    ///     file's own name cannot say which it is (#938).
    /// </summary>
    internal static bool IsAmbiguouslyPaired(string path) =>
        HasExtension(path, ".ts") || HasExtension(path, ".js");

    private static bool IsTypeScript(string path) =>
        HasExtension(path, ".ts") || HasExtension(path, ".tsx") || HasExtension(path, ".js")
        || HasExtension(path, ".jsx");

    private static bool HasExtension(string path, string extension) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     <paramref name="to" /> expressed relative to the directory <paramref name="from" />, always
    ///     forward-slashed.
    /// </summary>
    /// <remarks>
    ///     Only <paramref name="from" /> gets a trailing separator: it is always a directory, while
    ///     <paramref name="to" /> may be either. Appending one to a file path would make the file look
    ///     like a directory and shift every segment of the result.
    ///
    ///     Forward slashes because both outputs are JSON, where a Windows path is a string full of
    ///     escape sequences — and tsconfig accepts forward slashes on every platform.
    /// </remarks>
    private static string Relative(string from, string to)
    {
        var fromUri = new Uri(AppendSeparator(Path.GetFullPath(from)));
        var toUri = new Uri(Path.GetFullPath(to));
        var relative = Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString()).TrimEnd('/');
        return relative.Length == 0 ? "." : relative;
    }

    /// <summary>
    ///     A relative path with an explicit <c>./</c>, which TypeScript requires in a paths entry.
    /// </summary>
    /// <remarks>
    ///     Without it the mapping is a BARE specifier and TS5090 rejects it — the same distinction
    ///     that bit the bundler entry generation, where a bare specifier was resolved against
    ///     node_modules and the error named a package nobody wrote.
    /// </remarks>
    private static string Dotted(string relative) =>
        relative.StartsWith(".", StringComparison.Ordinal) ? relative : "./" + relative;

    private static string AppendSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
}
