using System;
using System.IO;

namespace Rask.Generators;

/// <summary>
///     How a file beside a component is paired with it: the same directory, the same name.
/// </summary>
/// <remarks>
///     One definition for every sibling-file convention — scoped CSS, scoped JS and an island's props
///     snapshot. They carried private copies of these lines, and the copies had already drifted: only the
///     scoped-JS one collapsed macOS's <c>/private</c> alias, so a CSS file reached through it paired
///     differently from a script beside the same component. A pairing rule that differs between
///     conventions is a file that pairs in one and silently not in another.
/// </remarks>
internal static class AssetPairing
{
    /// <summary>The directory of <paramref name="path" />, with forward slashes and macOS's alias collapsed.</summary>
    /// <remarks>
    ///     <para>
    ///         On macOS <c>/var</c> and <c>/tmp</c> are symbolic links into <c>/private</c>, and MSBuild and
    ///         the compiler do not agree on which spelling they hand over: a <c>.cs</c> can arrive as
    ///         <c>/var/…</c> while the file beside it arrives as <c>/private/var/…</c>. Two paths to one
    ///         directory would then pair nothing.
    ///     </para>
    ///     <para>
    ///         Resolving the link properly is not open to a generator — it must not touch the file system.
    ///         Collapsing the one alias macOS actually uses is what is available, and it is enough: the
    ///         prefix is fixed, documented, and applies to every path under it.
    ///     </para>
    /// </remarks>
    public static string NormalizeDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        dir = dir.Replace('\\', '/');

        return dir.StartsWith("/private/", StringComparison.Ordinal) ? dir.Substring("/private".Length) : dir;
    }

    /// <summary>The pairing key for a file or class named <paramref name="name" /> in <paramref name="dir" />.</summary>
    public static string MakeKey(string dir, string name) =>
        dir.Length == 0 ? name : dir + "/" + name;
}
