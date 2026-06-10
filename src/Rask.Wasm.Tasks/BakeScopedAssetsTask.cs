using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Rask.Wasm.Tasks;

/// <summary>
///     Walks every .dll under an AppBundle's framework folder, forces the generator-
///     emitted <c>__RaskScopedCssRegistration</c> / <c>__RaskScopedJsRegistration</c>
///     classes to fire their <c>RefreshAll</c> on the in-MSBuild copy of
///     <c>ScopedAssetRegistry</c>, then materialises every registered entry as a
///     <c>{BundleDir}/_rask/a/{hash}.{ext}</c> file.
///     <para>
///         Why this exists: the in-WASM-browser runtime computes per-component asset
///         hashes from <c>Rask.Example.Shared.dll</c> loaded into the .NET-in-Wasm
///         runtime. Without baking, the only thing that can serve those URLs is a
///         <c>Rask.Wasm.Hosting</c> host whose process also loaded the same assembly
///         (the <c>UseRask&lt;TApp&gt;()</c> generic forces that load). Standalone WASM
///         runs under WasmAppHost — a static-file dev launcher — and 404s on every
///         <c>/_rask/a/{hash}.{ext}</c> until the files exist on disk. This task writes
///         them at publish time so any static-file server works.
///     </para>
///     <para>
///         The task is invoked from <c>Rask.Wasm/build/Rask.Wasm.targets</c>'s
///         <c>_RaskBakeScopedAssets</c> target after <c>_GenerateAppBundle</c>. It is a
///         no-op when the bundle directory doesn't exist or has no <c>_framework</c>
///         subfolder.
///     </para>
/// </summary>
public sealed class BakeScopedAssetsTask : Task
{
    /// <summary>
    ///     Path to the AppBundle root where the <c>_rask/a/{hash}.{ext}</c> files will
    ///     be materialised (typically <c>$(WasmAppDir)</c>).
    /// </summary>
    [Required]
    public string BundleDir { get; set; } = string.Empty;

    /// <summary>
    ///     The .NET assemblies to scan. Each item's <c>Identity</c> is the path to a
    ///     pre-AOT <c>.dll</c> file. Wired from <c>@(_WasmAssembliesInternal)</c> in
    ///     the targets file — that's the MSBuild item the WASM SDK builds up while
    ///     preparing the bundle, before native AOT compilation rewrites them to
    ///     <c>.wasm</c> inside <c>AppBundle/_framework/</c>.
    /// </summary>
    [Required]
    public ITaskItem[] Assemblies { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    ///     When <c>true</c>, the bake fails the build (logs an error, returns
    ///     <c>false</c>) if the Rask registry resolved but produced zero files — i.e.
    ///     a Rask WASM project whose scoped assets silently failed to bake. Defaults to
    ///     <c>false</c>: a non-Rask project (no <c>Rask.Core</c>) still no-ops quietly,
    ///     and the build-time bake stays non-fatal. Wired to <c>true</c> only on the
    ///     <c>dotnet run</c> hook, where a missing bundle means the served app would
    ///     404 on every <c>/_rask/a/</c> URL — better to fail fast than serve a broken
    ///     standalone bundle.
    /// </summary>
    public bool FailOnEmpty { get; set; }

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

        var enumerateAll = registryType.GetMethod("EnumerateAll", BindingFlags.Static | BindingFlags.Public);
        if (enumerateAll is null)
        {
            Log.LogWarning("Rask asset bake: ScopedAssetRegistry.EnumerateAll not found — registry API mismatch.");
            return 0;
        }

        var outDir = Path.Combine(BundleDir, "_rask", "a");
        Directory.CreateDirectory(outDir);

        var written = 0;
        var entries = (IEnumerable)enumerateAll.Invoke(null, null)!;
        foreach (var entry in entries)
        {
            var entryType = entry.GetType();
            var hash = (string)entryType.GetProperty("Hash")!.GetValue(entry)!;
            var kind = entryType.GetProperty("Kind")!.GetValue(entry)!.ToString()!;
            var utf8 = entryType.GetProperty("Utf8")!.GetValue(entry)!;
            // ReadOnlyMemory<byte> → byte[]
            var toArray = utf8.GetType().GetMethod("ToArray")!;
            var bytes = (byte[])toArray.Invoke(utf8, null)!;

            // AssetKind enum: lowercase ext matching the existing endpoint route shape.
            var ext = string.Equals(kind, "Css", StringComparison.OrdinalIgnoreCase) ? "css" : "js";
            var path = Path.Combine(outDir, hash + "." + ext);
            File.WriteAllBytes(path, bytes);
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
