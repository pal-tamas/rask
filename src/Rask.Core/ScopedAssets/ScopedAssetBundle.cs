namespace Rask.Core.ScopedAssets;

/// <summary>
///     The published WASM bundle's baked <c>_rask/a/{hash}.{ext}</c> files, as a lookup both ASP.NET
///     hosts can share.
///     <para>
///         A host process's in-memory <see cref="ScopedAssetRegistry" /> only carries assets from the
///         assemblies that process actually loaded, which is a strict subset of the in-WASM-runtime set —
///         so the hash the browser asks for is routinely unknown on the host side, and the authoritative
///         copy is the file the publish baked into the bundle. <c>Rask.Wasm.Hosting</c> has always served
///         that fallback; <c>Rask.Server</c> never knew about it, because until now the two never ran in
///         one app.
///     </para>
///     <para>
///         They do now: a wasm-hosted app that mounts the operator dashboard runs both hosts, and only
///         one of them can own the shared <c>/_rask/a/{hash}.{ext}</c> route. Parking the bundle
///         directory here is what makes that ownership irrelevant — either host's handler resolves the
///         same bytes, so whichever maps the endpoint first behaves identically and the order of the two
///         <c>UseRask</c> lines stops being load-bearing.
///     </para>
///     <para>
///         Static rather than DI for the same reason <see cref="Live.LiveOptions.PathBase" /> is: it
///         backs the process-wide content-addressed asset registry, which is itself static.
///     </para>
/// </summary>
public static class ScopedAssetBundle
{
    /// <summary>
    ///     Directory holding the published bundle, or <see langword="null" /> when this process serves no
    ///     baked bundle (a plain <c>Rask.Server</c> app — where every lookup below returns
    ///     <see langword="null" /> and the registry stays the only source, exactly as before).
    ///     Set by <c>Rask.Wasm.Hosting</c>'s <c>UseRask</c> once it has resolved the bundle.
    /// </summary>
    /// <remarks>
    ///     <b>Process-wide, and last writer wins.</b> An app serves one published bundle, so a single
    ///     value is right there — a WASM host mounted alongside a server-rendered dashboard still has
    ///     exactly one bundle to point at. It is <em>tests</em> that stand up a host per case: two of
    ///     them overlapping re-point this at each other's temp directory mid-request, and the loser
    ///     404s on a file it wrote itself. A suite that starts more than one host must therefore keep
    ///     every such class in one xUnit collection, not merely reset this between them.
    /// </remarks>
    public static string? BakedDirectory { get; set; }

    /// <summary>
    ///     The baked file for a content hash, or <see langword="null" /> when there is no bundle, the
    ///     hash is malformed, or the file is absent.
    /// </summary>
    /// <remarks>
    ///     The hash is validated as fixed-length lowercase hex before it reaches
    ///     <see cref="Path.Combine(string, string, string, string)" />, so it cannot contain a separator
    ///     or a <c>..</c> segment and cannot traverse outside <see cref="BakedDirectory" />. That check is
    ///     the only thing standing between a routed URL segment and the filesystem, so it is done here
    ///     rather than left to each caller.
    /// </remarks>
    public static string? FindBakedFile(string? hash, AssetKind kind)
    {
        if (BakedDirectory is not { } directory || !IsContentHash(hash))
        {
            return null;
        }

        var path = Path.Combine(directory, "_rask", "a", hash + Extension(kind));
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    ///     The precompressed sibling the WASM publish emits next to a baked asset (<c>.br</c> / <c>.gz</c>)
    ///     for an already-negotiated encoding, or <see langword="null" /> when there isn't one to serve.
    /// </summary>
    public static string? FindPrecompressedSibling(string bakedPath, string? encoding)
    {
        var sibling = encoding switch
        {
            "br" => bakedPath + ".br",
            "gzip" => bakedPath + ".gz",
            _ => null,
        };

        return sibling is not null && File.Exists(sibling) ? sibling : null;
    }

    /// <summary>The on-disk extension for an asset kind — the same one the bake wrote.</summary>
    public static string Extension(AssetKind kind) => kind == AssetKind.Css ? ".css" : ".js";

    /// <summary>The content type for an asset kind, shared so both hosts answer byte-identically.</summary>
    public static string ContentType(AssetKind kind) =>
        kind == AssetKind.Css ? "text/css; charset=utf-8" : "text/javascript; charset=utf-8";

    /// <summary>
    ///     Whether a routed segment is a well-formed content hash: exactly
    ///     <see cref="ScopedAssetRegistry.HashHexLength" /> lowercase hex characters. Deliberately strict
    ///     — an unknown hash must 404 rather than leak whether a path exists.
    /// </summary>
    public static bool IsContentHash([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value)
    {
        if (value is null || value.Length != ScopedAssetRegistry.HashHexLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            {
                return false;
            }
        }

        return true;
    }
}
