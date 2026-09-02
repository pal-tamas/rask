namespace Rask.Meta.Hosting;

/// <summary>
///     A directory of built client assets that Kestrel serves itself instead of forwarding.
/// </summary>
/// <param name="RequestPath">
///     The URL prefix the directory answers under. Empty means the site root.
/// </param>
/// <param name="Directory">
///     Where the files are, relative to <see cref="MetaHostingOptions.AppDirectory" />.
/// </param>
/// <remarks>
///     Every framework here builds its client assets into a directory of its own, and every one of them
///     is content-hashed. Serving them from Kestrel rather than forwarding costs a hop less per asset,
///     puts them behind the immutable cache rules this host already knows how to write, and — for Next
///     — is the difference between working and not, because standalone output deliberately omits them
///     on the assumption that a CDN is in front.
/// </remarks>
public sealed record StaticRoot(string RequestPath, string Directory);
