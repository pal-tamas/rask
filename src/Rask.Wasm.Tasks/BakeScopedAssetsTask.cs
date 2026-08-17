using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Rask.Wasm.Tasks;

/// <summary>
///     Walks the provided .NET assemblies, forces the generator-emitted
///     <c>__RaskScopedCssRegistration</c> / <c>__RaskScopedJsRegistration</c> classes to fire
///     their <c>RefreshAll</c> on the in-MSBuild copy of <c>ScopedAssetRegistry</c>, then
///     materialises every registered entry as a <c>{BundleDir}/_rask/a/{hash}.{ext}</c> file.
///     <para>
///         Why this exists: the in-WASM-browser runtime computes per-component asset
///         hashes from <c>Rask.Example.Shared.dll</c> loaded into the .NET-in-Wasm
///         runtime. Without baking, the only thing that can serve those URLs is a
///         <c>Rask.Wasm.Hosting</c> host whose process also loaded the same assembly
///         (the <c>UseRask&lt;TApp&gt;()</c> generic forces that load). Standalone WASM
///         runs under WasmAppHost — a static-asset dev server — and 404s on every
///         <c>/_rask/a/{hash}.{ext}</c> unless the files are registered/served. This task
///         writes them into a staging dir that the targets register as static web assets,
///         so any static-file server works.
///     </para>
///     <para>
///         The task is invoked from <c>Rask.Wasm/build/Rask.Wasm.targets</c>'s
///         <c>_RaskBakeScopedStaticWebAssets</c> target, which registers the staged files as
///         computed static web assets. It is a no-op when the bundle directory doesn't exist.
///     </para>
/// </summary>
public sealed class BakeScopedAssetsTask : Task
{
    /// <summary>
    ///     Path to the directory under which the <c>_rask/a/{hash}.{ext}</c> files will
    ///     be materialised (the framework passes an intermediate staging dir).
    /// </summary>
    [Required]
    public string BundleDir { get; set; } = string.Empty;

    /// <summary>
    ///     The .NET assemblies to scan. Each item's <c>Identity</c> is the path to a
    ///     pre-trim <c>.dll</c> file. Wired from <c>@(IntermediateAssembly)</c> (the app's own
    ///     compiled assembly) plus <c>@(ReferenceCopyLocalPaths)</c> (the referenced Rask
    ///     assemblies) in the targets file — the pre-trim build outputs, whose
    ///     <c>[ModuleInitializer]</c>s re-fire cleanly inside the MSBuild host.
    /// </summary>
    [Required]
    public ITaskItem[] Assemblies { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    ///     When <c>true</c>, the bake fails the build (logs an error, returns
    ///     <c>false</c>) if the Rask registry resolved but produced zero files — i.e.
    ///     a Rask WASM project whose scoped assets silently failed to bake. Defaults to
    ///     <c>false</c>: a non-Rask project (no <c>Rask.Core</c>) still no-ops quietly,
    ///     and the build-time bake stays non-fatal.
    /// </summary>
    /// <remarks>
    ///     <b>Nothing sets this today</b> — the single call site in <c>Rask.Wasm.targets</c> leaves it at
    ///     its default, so the guard below never fires in a real build. It is kept because the check is
    ///     cheap and correct for what it covers, and a future caller may want it.
    ///     <para>
    ///         It is deliberately <b>not</b> the guard against a published bundle missing its scoped
    ///         assets, which is the failure that actually bites (#650/#652): there the bake runs and
    ///         writes its files perfectly well, and the break is between staging and the published
    ///         output — invisible from inside this task. <c>_RaskVerifyPublishedScopedAssets</c> in
    ///         <c>Rask.Wasm.targets</c> covers that, by comparing what was staged against what shipped.
    ///     </para>
    /// </remarks>
    public bool FailOnEmpty { get; set; }

    /// <summary>
    ///     Assemblies skipped because this MSBuild process had already loaded one of the same simple name.
    ///     Non-empty means the bake was working from an incomplete view of the app — see the check in
    ///     <see cref="Execute" />.
    /// </summary>
    private readonly List<string> _skippedAlreadyLoaded = new();

    /// <summary>
    ///     Runs the bake: collects every scoped CSS/JS asset the component assemblies registered and writes
    ///     them where the published bundle serves them from.
    /// </summary>
    /// <returns><see langword="true" /> when the bake succeeded; <see langword="false" /> fails the build.</returns>
    public override bool Execute()
    {
        if (string.IsNullOrEmpty(BundleDir))
        {
            Log.LogMessage(MessageImportance.Low, "Rask asset bake: BundleDir empty — skipping.");
            return true;
        }

        if (!Directory.Exists(BundleDir))
        {
            Log.LogMessage(MessageImportance.Low,
                $"Rask asset bake: bundle directory not found at '{BundleDir}' — skipping.");
            return true;
        }

        if (Assemblies.Length == 0)
        {
            Log.LogMessage(MessageImportance.Low,
                "Rask asset bake: no Assemblies passed in — skipping (not a Rask WASM project).");
            return true;
        }

        try
        {
            var written = BakeFromAssemblies(out var registryResolved);
            Log.LogMessage(MessageImportance.High,
                $"Rask asset bake: wrote {written} file(s) under '{Path.Combine(BundleDir, "_rask", "a")}'.");

            // The bake produced nothing, we skipped an assembly because this process had already loaded one
            // by that name, AND the registry itself was never read. That combination is the MSBuild
            // node-reuse race (#650): LoadFrom throws on a reused worker, the registry is never reached, and
            // the bake quietly writes an empty bundle. Measured at roughly one publish in three, and silent —
            // the app then boots with every /_rask/a/ URL 404ing, which reads as a broken app rather than a
            // broken build. Failing here is what stops that shipping.
            //
            // !registryResolved is load-bearing, and was missing. Without it this fires on a project that
            // legitimately has NO scoped assets (Rask.Example.Wasm.Jobs is one) the moment ANY assembly is
            // skipped — and the skip need not be one that could ever hold a scoped asset. A Microsoft
            // .Extensions bump was enough: the app then carries a DependencyModel newer than the one MSBuild
            // already has loaded, LoadFrom throws on identity, and a build that was entirely correct failed
            // on an assembly that cannot contain a registration. If the registry WAS read, "zero files" is
            // an answer, not a failure — and the FailOnEmpty check below is what speaks for the projects
            // that assert they should have produced some.
            //
            // Still conditioned on written == 0: a bake that produced its files despite skipping something
            // is not known to be wrong, and failing it would turn a real fix into a new source of false
            // build breaks.
            if (IsNodeReuseBakeFailure(written, _skippedAlreadyLoaded.Count, registryResolved))
            {
                Log.LogError(
                    "Rask asset bake: zero /_rask/a/ files were written because " +
                    $"{string.Join(", ", _skippedAlreadyLoaded)} could not be loaded — this MSBuild worker " +
                    "had already loaded an assembly of that name (node reuse), so the scoped-asset registry " +
                    "was never read. The published app would 404 on every scoped CSS/JS URL. Re-run the " +
                    "publish with -nodeReuse:false, or from a fresh MSBuild process.");
                return false;
            }

            if (FailOnEmpty && registryResolved && written == 0)
            {
                Log.LogError("Rask asset bake: the Rask registry resolved but zero /_rask/a/ files " +
                             $"were written under '{BundleDir}'. The standalone WASM app would 404 on every " +
                             "scoped-asset URL. Failing the build (FailOnEmpty=true).");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // Treat bake failure as a warning rather than a build break: missing baked
            // assets fall back to the in-process endpoint (Rask.Wasm.Hosting case) or
            // surface as 404s in the browser (standalone WASM case). Stopping the
            // build because of a bake hiccup would block far more than it'd protect.
            Log.LogWarning($"Rask asset bake: failed — '{ex.Message}'. Build continues; standalone " +
                           "WASM hosting may 404 on /_rask/a/ URLs until the bake succeeds.");
            return true;
        }
    }

    /// <summary>
    ///     Whether an empty bake is the MSBuild node-reuse race (#650) rather than a project that simply
    ///     has no scoped assets.
    /// </summary>
    /// <remarks>
    ///     Extracted so the decision can be pinned by a test. It is three booleans and it has already been
    ///     wrong once, in a way no build caught: without <paramref name="registryResolved" /> it fired on any
    ///     project with no scoped assets the moment ANY assembly was skipped, including one that could never
    ///     hold a registration. A Microsoft.Extensions version bump was enough to trigger it.
    /// </remarks>
    internal static bool IsNodeReuseBakeFailure(int written, int skippedAlreadyLoaded, bool registryResolved)
        => written == 0 && skippedAlreadyLoaded > 0 && !registryResolved;

    private int BakeFromAssemblies(out bool registryResolved)
    {
        registryResolved = false;
        // Track every directory we see assemblies in, so the AssemblyResolve fallback
        // can satisfy late-bound references (e.g. one assembly's [ModuleInitializer]
        // touching a type from another) without us having to load every dep upfront.
        var searchDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Assemblies)
        {
            var dir = Path.GetDirectoryName(item.ItemSpec);
            if (!string.IsNullOrEmpty(dir))
            {
                searchDirs.Add(dir);
            }
        }

        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var simpleName = new AssemblyName(args.Name).Name;
            if (simpleName is null)
            {
                return null;
            }

            foreach (var dir in searchDirs)
            {
                var candidate = Path.Combine(dir, simpleName + ".dll");
                if (File.Exists(candidate))
                {
                    try { return Assembly.LoadFrom(candidate); }
                    catch
                    {
                        /* ignore */
                    }
                }
            }

            return null;
        };

        Type? registryType = null;
        var registrationTypes = new List<Type>();
        foreach (var item in Assemblies)
        {
            var dllPath = item.ItemSpec;
            if (!File.Exists(dllPath))
            {
                Log.LogMessage(MessageImportance.Low,
                    $"Rask asset bake: skipping missing assembly '{dllPath}'");
                continue;
            }

            Assembly? assembly;
            try
            {
                assembly = Assembly.LoadFrom(dllPath);
            }
            catch (Exception ex)
            {
                Log.LogMessage(MessageImportance.Low,
                    $"Rask asset bake: skipping {Path.GetFileName(dllPath)} — {ex.GetType().Name}: {ex.Message}");

                // Remember the ones that were already loaded into this process. MSBuild reuses its worker
                // nodes, so a publish can land on a node that loaded an assembly of the same simple name
                // during an earlier build — LoadFrom then throws and we skip an assembly whose scoped
                // assets we needed. Skipping is the right local behaviour (recovering the loaded instance
                // would bake a PREVIOUS build's state, which is worse than baking none), but it must not
                // pass silently when it costs us the whole bundle: see the check in Execute.
                if (ex is FileLoadException)
                {
                    _skippedAlreadyLoaded.Add(Path.GetFileName(dllPath));
                }

                continue;
            }

            if (registryType is null && assembly.GetName().Name == "Rask.Core")
            {
                registryType = assembly.GetType("Rask.Core.ScopedAssets.ScopedAssetRegistry", false);
            }

            // Source-generator-emitted registration classes are top-level (no namespace);
            // collect them so we can re-fire RefreshAll after invalidating the registry.
            foreach (var name in new[] { "__RaskScopedCssRegistration", "__RaskScopedJsRegistration" })
            {
                var t = assembly.GetType(name, false);
                if (t is not null)
                {
                    registrationTypes.Add(t);
                }
            }
        }

        if (registryType is null)
        {
            Log.LogMessage(MessageImportance.Low,
                "Rask asset bake: Rask.Core not found in bundle. Skipping (not a Rask WASM project).");
            return 0;
        }

        registryResolved = true;

        // Reset before re-firing — module initializers ran once per ALC load; calling
        // RefreshAll explicitly guarantees a clean snapshot regardless of MSBuild
        // worker reuse across builds.
        InvokeStatic(registryType, "InvalidateAllCss");
        InvokeStatic(registryType, "InvalidateAllJs");

        foreach (var regType in registrationTypes)
        {
            var refreshAll = regType.GetMethod("RefreshAll",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            try
            {
                refreshAll?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Log.LogMessage(MessageImportance.Low,
                    $"Rask asset bake: RefreshAll on {regType.Assembly.GetName().Name}/{regType.Name} threw " +
                    $"{ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        // The runtime emits a single <link>/<script> per kind at the concatenated bundle's content
        // hash, so the bake materialises exactly those two files — GetBundleHash + GetByHash are the
        // same registry methods the runtime calls, so the on-disk file name matches the URL the
        // browser requests byte-for-byte.
        var assetKindType = registryType.Assembly.GetType("Rask.Core.ScopedAssets.AssetKind", false);
        var getBundleHash = registryType.GetMethod("GetBundleHash", BindingFlags.Static | BindingFlags.Public);
        var getByHash = registryType.GetMethod("GetByHash", BindingFlags.Static | BindingFlags.Public);
        if (assetKindType is null || getBundleHash is null || getByHash is null)
        {
            Log.LogWarning("Rask asset bake: ScopedAssetRegistry bundle API not found — registry API mismatch.");
            return 0;
        }

        var outDir = Path.Combine(BundleDir, "_rask", "a");
        Directory.CreateDirectory(outDir);

        var written = 0;
        foreach (var (kindName, ext) in new[] { ("Css", "css"), ("Js", "js") })
        {
            var kind = Enum.Parse(assetKindType, kindName);
            var hash = (string)getBundleHash.Invoke(null, new[] { kind })!;
            if (string.IsNullOrEmpty(hash))
            {
                continue; // no registered asset of this kind — no bundle file.
            }

            var assetBytes = getByHash.Invoke(null, new[] { hash, kind });
            if (assetBytes is null)
            {
                continue;
            }

            var utf8 = assetBytes.GetType().GetProperty("Utf8")!.GetValue(assetBytes)!;
            // ReadOnlyMemory<byte> → byte[]
            var bytes = (byte[])utf8.GetType().GetMethod("ToArray")!.Invoke(utf8, null)!;
            File.WriteAllBytes(Path.Combine(outDir, hash + "." + ext), bytes);
            written++;
        }

        return written;
    }

    private static void InvokeStatic(Type type, string method)
    {
        var m = type.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        m?.Invoke(null, null);
    }
}
