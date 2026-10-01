using System;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

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
        // A LINKED source (`<Compile Include="..\..\src\App\**\*.cs"/>`) reaches the compiler with its `..`
        // segments intact, while MSBuild's %(FullPath) tag on a scoped script is already collapsed — two
        // spellings of one folder. GetFullPath on a rooted path only rewrites the string; it reads nothing.
        if (Path.IsPathRooted(path))
        {
            path = Path.GetFullPath(path);
        }

        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        dir = dir.Replace('\\', '/');

        return dir.StartsWith("/private/", StringComparison.Ordinal) ? dir.Substring("/private".Length) : dir;
    }

    /// <summary>The pairing key for a file or class named <paramref name="name" /> in <paramref name="dir" />.</summary>
    public static string MakeKey(string dir, string name) =>
        dir.Length == 0 ? name : dir + "/" + name;

    /// <summary>
    ///     A location in a sibling file that is not part of the compilation.
    /// </summary>
    /// <remarks>
    ///     The alternative is <c>Location.None</c>, which leaves an error about <c>Counter.css</c> with nothing to
    ///     click and nothing to blame.
    /// </remarks>
    public static Location SourceLocation(string path) =>
        Location.Create(path, new TextSpan(0, 0), new LinePositionSpan(default, default));

    /// <summary>Appends <paramref name="value" /> as a C# verbatim string literal.</summary>
    public static void AppendVerbatimStringLiteral(StringBuilder sb, string value)
    {
        sb.Append("@\"");
        foreach (var ch in value)
        {
            if (ch == '"')
            {
                sb.Append("\"\"");
            }
            else
            {
                sb.Append(ch);
            }
        }

        sb.Append('"');
    }
}
