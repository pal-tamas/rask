namespace Rask.Core.ScopedAssets;

/// <summary>
///     The shape of a baked <c>_rask/a/{hash}.{ext}</c> file: its extension, its content type, and the
///     check that keeps a routed hash from reaching the filesystem as anything but a hash.
/// </summary>
/// <remarks>
///     A WebAssembly publish writes every scoped asset under <c>_rask/a/</c>, named by content hash. A
///     <c>Rask.Server</c> process that shares a host with such a bundle — the operator dashboard beside a
///     WebAssembly app — owns the <c>/_rask/a/{hash}.{ext}</c> route, and its registry only carries what
///     that process loaded, so a hash it lacks is answered from the app's web root, where
///     <c>UseRaskSpa</c> places the bundle's files.
/// </remarks>
public static class ScopedAssetBundle
{
    /// <summary>The on-disk extension for an asset kind — the same one the bake wrote.</summary>
    public static string Extension(AssetKind kind) => kind == AssetKind.Css ? ".css" : ".js";

    /// <summary>The content type for an asset kind, shared so a baked file and a registry entry answer alike.</summary>
    public static string ContentType(AssetKind kind) =>
        kind == AssetKind.Css ? "text/css; charset=utf-8" : "text/javascript; charset=utf-8";

    /// <summary>
    ///     Whether a routed segment is a well-formed content hash: exactly
    ///     <see cref="ScopedAssetRegistry.HashHexLength" /> lowercase hex characters. Deliberately strict
    ///     — an unknown hash must 404 rather than leak whether a path exists, and a value that passes
    ///     cannot hold a separator or a <c>..</c> segment, which is what makes it safe to join onto a path.
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
