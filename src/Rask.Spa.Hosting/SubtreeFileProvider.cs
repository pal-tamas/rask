using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Rask.Spa.Hosting;

/// <summary>
///     One subtree of another file provider, and nothing else.
/// </summary>
/// <remarks>
///     What <see cref="RaskSpaEndpointExtensions.UseRaskSpa" /> adds to the web root of a host that also
///     runs <c>Rask.Server</c>. Only <c>_rask/a/</c> has to be reachable there; handing over the whole
///     bundle would let a plain <c>UseStaticFiles()</c> serve it again at the site root, outside the
///     prefix the app was mounted under.
/// </remarks>
internal sealed class SubtreeFileProvider(IFileProvider inner, string subtree) : IFileProvider
{
    public IFileInfo GetFileInfo(string subpath) =>
        Contains(subpath) ? inner.GetFileInfo(subpath) : new NotFoundFileInfo(subpath);

    public IDirectoryContents GetDirectoryContents(string subpath) =>
        Contains(subpath) ? inner.GetDirectoryContents(subpath) : NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    private bool Contains(string subpath)
    {
        var trimmed = subpath.TrimStart('/', '\\');
        if (!trimmed.StartsWith(subtree, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A prefix check alone lets "_rask/a/../index.html" climb back out of the subtree: the inner
        // provider only refuses a path that leaves ITS root, which is the whole bundle. Both separators,
        // because only '/' is normalised out of a request path before it gets here.
        foreach (var segment in trimmed.Split(['/', '\\']))
        {
            if (segment == "..")
            {
                return false;
            }
        }

        return true;
    }
}
