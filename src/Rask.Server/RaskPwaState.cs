using Rask.Core.Browser;
namespace Rask.Server;

/// <summary>
///     Holds the opted-in <see cref="WebAppManifest" />. Its presence in DI is the "PWA enabled" flag the
///     request pipeline checks — the manifest/service-worker endpoints and the head contribution are only
///     wired when this singleton is registered (via <see cref="RaskPwaExtensions.AddRaskPwa" />).
/// </summary>
internal sealed class RaskPwaState(WebAppManifest manifest)
{
    public WebAppManifest Manifest { get; } = manifest;
}
