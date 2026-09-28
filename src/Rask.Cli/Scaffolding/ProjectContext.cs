using System.Text.RegularExpressions;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// The .NET project the scaffolder is generating into: its directory and root namespace. Given a target
/// directory it derives the folder-based namespace a generated file should declare, matching the C#
/// convention (root namespace + the folder path relative to the project).
/// </summary>
internal sealed partial class ProjectContext(
    string projectDirectory,
    string rootNamespace,
    bool isBrowser = false)
{
    private static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public string ProjectDirectory { get; } = projectDirectory;

    public string RootNamespace { get; } = rootNamespace;

    /// <summary>
    /// Whether this project is a browser (WebAssembly) app rather than a server one.
    /// </summary>
    /// <remarks>
    /// Detected from the project file rather than asked for: the answer was already decided by
    /// <c>rask new</c>, and asking again is a second thing to get out of sync. It changes what
    /// scaffolding can honestly tell you to do — a browser app has no design-time database for
    /// <c>rask db</c> to migrate, and its database needs <c>AddRaskBrowserSqlite</c> to survive a
    /// reload at all.
    /// </remarks>
    public bool IsBrowser { get; } = isBrowser;

    /// <summary>The namespace a file in <paramref name="targetDirectory"/> should declare.</summary>
    public string NamespaceFor(string targetDirectory)
    {
        var relative = Path.GetRelativePath(ProjectDirectory, targetDirectory);
        if (relative is "." or "")
        {
            return RootNamespace;
        }

        var parts = new List<string> { RootNamespace };
        foreach (var segment in relative.Split(PathSeparators))
        {
            // A target outside the project (a leading "..") can't map to a child namespace — fall back
            // to the root namespace rather than emitting something invalid.
            if (segment is "" or ".")
            {
                continue;
            }

            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                return RootNamespace;
            }

            if (Identifiers.ToNamespacePart(segment) is { } part)
            {
                parts.Add(part);
            }
        }

        return string.Join('.', parts);
    }

    [GeneratedRegex(@"<RootNamespace>\s*(?<value>.+?)\s*</RootNamespace>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RootNamespaceRegex();

    // A browser TFM on either the singular or plural element. Matched by the "-browser" suffix rather
    // than the framework version, so a bump doesn't silently stop detecting it — but scoped to the
    // element, because the bare string also occurs in comments, constants and package ids.
    [GeneratedRegex(@"<TargetFrameworks?>[^<]*-browser", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BrowserTargetFrameworkRegex();

    // A reference to the WASM host itself, as a package (Include="Rask.Wasm") or as a project
    // (Include="..\..\src\Rask.Wasm\Rask.Wasm.csproj") — the repo's own samples use the latter.
    // Anchored on the closing quote so it does NOT match a longer package id such as Rask.Wasm.Tasks.
    [GeneratedRegex(@"Include=""(?:[^""]*[\\/])?Rask\.Wasm(?:\.csproj)?""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WasmHostReferenceRegex();

    /// <summary>
    /// Whether a project file describes a browser (WASM) app.
    /// </summary>
    /// <remarks>
    /// Three independent signals, because an app can be recognisably a browser app by any of them: the
    /// browser target framework, the framework's own <c>RaskWasm</c> marker, or a reference to the WASM
    /// host. Matching any one is deliberate — a project that is a browser app by only one signal is still
    /// a browser app.
    /// <para>
    /// Each signal is matched precisely rather than as a substring, because a false positive here is not
    /// cosmetic: it hands a server project the browser next-steps and adds <c>Rask.SQLite.Browser</c> to
    /// it, which doesn't resolve there — and the <b>Server</b> half of a client-plus-host solution is
    /// precisely the project a background job belongs in. Keeping the closing quote on the package check
    /// is what stops a longer <c>Rask.Wasm.*</c> id matching.
    /// </para>
    /// </remarks>
    internal static bool DetectBrowser(string csprojText)
    {
        ArgumentNullException.ThrowIfNull(csprojText);

        return BrowserTargetFrameworkRegex().IsMatch(csprojText)
            // The value, not just the element: <RaskWasm>false</RaskWasm> asserts the opposite.
            || csprojText.Contains("<RaskWasm>true</RaskWasm>", StringComparison.OrdinalIgnoreCase)
            || WasmHostReferenceRegex().IsMatch(csprojText);
    }

    internal static string ReadRootNamespace(IFileSystem fileSystem, string csprojPath)
    {
        // An explicit <RootNamespace> wins; otherwise the SDK default is the project file name. Either
        // way the value is sanitized into a valid namespace — an explicit "1Store" or "My-App" must not
        // reach a generated file verbatim (it wouldn't compile).
        var match = RootNamespaceRegex().Match(fileSystem.ReadAllText(csprojPath));
        var raw = match.Success ? match.Groups["value"].Value : Path.GetFileNameWithoutExtension(csprojPath);
        return SanitizeNamespace(raw);
    }

    private static string SanitizeNamespace(string raw)
    {
        var parts = raw
            .Split('.')
            .Select(Identifiers.ToNamespacePart)
            .Where(part => part is not null);

        var joined = string.Join('.', parts);
        return joined.Length == 0 ? "App" : joined;
    }
}
