using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Rask.TypeScript.Tasks;

/// <summary>The tools Rask fetches to work with TypeScript without npm.</summary>
/// <remarks>
///     esbuild and tsgo are deliberately separate tools rather than one. esbuild strips types and bundles but
///     does not type-check; tsgo type-checks but does not bundle. Using esbuild alone would mean TypeScript
///     with none of the guarantee that makes it worth writing, and using tsgo alone would mean shipping
///     unbundled, unminified modules. The compiler's JavaScript API is a third thing again: the one stable way
///     to ask the checker what a type IS, which is how a package island reads a component's props.
/// </remarks>
public enum TypeScriptTool
{
    /// <summary>The bundler: type-strip, resolve imports, downlevel, minify.</summary>
    Esbuild,

    /// <summary>The type checker — the native Go build of the TypeScript compiler.</summary>
    Tsgo,

    /// <summary>
    ///     The TypeScript compiler as a JavaScript library, for its programmatic checker API.
    /// </summary>
    /// <remarks>
    ///     Not a binary: it runs under Node, and only where a project already has Node because it builds
    ///     islands. tsgo has no stable programmatic API yet, and a props snapshot needs the checker's own
    ///     answer about a type rather than a reading of the declaration text.
    /// </remarks>
    TypeScript,
}

/// <summary>The operating systems these tools publish native builds for.</summary>
/// <remarks>Public only so the tests can name it in a theory; this assembly is build-only.</remarks>
public enum ToolOs
{
    MacOs,
    Linux,
    Windows,
}

/// <summary>
///     Which package this machine needs for a tool, where it comes from, and where it is cached.
/// </summary>
/// <remarks>
///     <para>
///         Pure, and separated from the task that downloads it, because the platform mapping is the part
///         with judgement in it — a wrong package name is a 404 several minutes into someone's first
///         build, and the message npm's registry returns names neither Rask nor TypeScript.
///     </para>
///     <para>
///         Every tool is distributed as an npm package, but nothing here needs npm. An npm package is a
///         gzipped tarball at a predictable URL with a published checksum, so Rask fetches and verifies it
///         directly, exactly as <c>Rask.Tailwind.Tasks</c> fetches the Tailwind standalone CLI. The native
///         tools then need no JavaScript runtime at all; the compiler library needs Node, which a project
///         building islands has by definition.
///     </para>
/// </remarks>
internal static class TypeScriptTools
{
    /// <summary>The npm registry. Overridable so a build behind a mirror is not a build that fails.</summary>
    public const string DefaultRegistry = "https://registry.npmjs.org";

    /// <summary>Whether a tool is a native executable, published per platform.</summary>
    public static bool IsNative(TypeScriptTool tool) => tool != TypeScriptTool.TypeScript;

    /// <summary>
    ///     The package holding this tool for a platform, or null when none is published.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         esbuild covers far more platforms than Tailwind does — including <c>win32-arm64</c>, which
    ///         means, unlike the Tailwind resolver, there is no need to hand Windows-on-ARM an x64 binary
    ///         and let it emulate.
    ///     </para>
    ///     <para>
    ///         There is deliberately <b>no musl special case</b>. Both binaries are statically linked Go,
    ///         so the linux-x64 build runs on Alpine as it does on Debian. Tailwind needs
    ///         <c>-musl</c> variants and a loader probe to pick between them; copying that here would be
    ///         cargo cult, and a wrong guess would fail on the distro least able to explain why.
    ///     </para>
    ///     <para>
    ///         The compiler library is one package for every platform, so it is answered before the platform
    ///         is looked at — a 32-bit or unknown machine can still read a component's props.
    ///     </para>
    /// </remarks>
    public static string? PackageName(TypeScriptTool tool, ToolOs os, Architecture architecture)
    {
        if (tool == TypeScriptTool.TypeScript)
        {
            return "typescript";
        }

        var slug = PlatformSlug(tool, os, architecture);
        if (slug is null)
        {
            return null;
        }

        return tool switch
        {
            TypeScriptTool.Esbuild => "@esbuild/" + slug,
            TypeScriptTool.Tsgo => "@typescript/native-preview-" + slug,
            _ => null,
        };
    }

    /// <summary>The <c>{os}-{arch}</c> half of the package name, shared by both native tools' naming schemes.</summary>
    private static string? PlatformSlug(TypeScriptTool tool, ToolOs os, Architecture architecture)
    {
        var arch = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",

            // esbuild publishes 32-bit builds; tsgo does not. A 32-bit machine can therefore bundle but
            // not type-check, which the caller reports as "no build for this platform" rather than
            // pretending the checked half ran.
            Architecture.X86 when tool == TypeScriptTool.Esbuild => "ia32",
            Architecture.Arm when tool == TypeScriptTool.Esbuild => "arm",
            _ => null,
        };

        if (arch is null)
        {
            return null;
        }

        return os switch
        {
            ToolOs.MacOs => "darwin-" + arch,
            ToolOs.Linux => "linux-" + arch,
            ToolOs.Windows => "win32-" + arch,
            _ => null,
        };
    }

    /// <summary>
    ///     Where the entry point sits inside the extracted package, relative to its root.
    /// </summary>
    /// <remarks>
    ///     The tools disagree, and so does esbuild with itself: esbuild is <c>bin/esbuild</c> on Unix
    ///     but <c>esbuild.exe</c> at the package root on Windows, with no <c>bin</c> directory at all.
    ///     Assuming one layout works on a developer's machine and fails on somebody else's, which is the
    ///     worst time to find out — so every layout is stated here and covered by a test. The compiler
    ///     library is the same file everywhere.
    /// </remarks>
    public static string ExecutablePath(TypeScriptTool tool, ToolOs os)
    {
        var windows = os == ToolOs.Windows;
        return tool switch
        {
            TypeScriptTool.Esbuild => windows ? "esbuild.exe" : Path.Combine("bin", "esbuild"),
            TypeScriptTool.Tsgo => windows ? Path.Combine("lib", "tsgo.exe") : Path.Combine("lib", "tsgo"),
            TypeScriptTool.TypeScript => Path.Combine("lib", "typescript.js"),
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };
    }

    /// <summary>
    ///     Whether the whole extracted tree matters, or only the one entry point.
    /// </summary>
    /// <remarks>
    ///     tsgo ships <c>lib/lib.dom.d.ts</c> and its ~110 siblings beside the binary and resolves
    ///     <c>"lib"</c> from its own location, so extracting only <c>lib/tsgo</c> yields a compiler that
    ///     reports every DOM type as undefined. The compiler library resolves the same <c>lib.*.d.ts</c> files
    ///     from beside <c>typescript.js</c>, for the same reason. esbuild genuinely is one file. All are
    ///     extracted whole; this exists to say why that is not incidental.
    /// </remarks>
    public static bool NeedsWholePackage(TypeScriptTool tool) => tool != TypeScriptTool.Esbuild;

    /// <summary>The metadata document for one version — ~2 KB, versus a megabyte for the full packument.</summary>
    public static string VersionDocumentUrl(string registry, string packageName, string version) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}/{1}/{2}",
            registry.TrimEnd('/'),
            EncodePackageName(packageName),
            version);

    /// <summary>The tarball URL for a pinned version.</summary>
    /// <remarks>
    ///     Derived rather than read from the version document, so the download URL cannot be redirected
    ///     by whatever answered the metadata request. The document is consulted for the checksum only.
    /// </remarks>
    public static string TarballUrl(string registry, string packageName, string version)
    {
        // "@esbuild/darwin-arm64" -> tarballs are served at "@esbuild/darwin-arm64/-/darwin-arm64-0.28.2.tgz":
        // the scope is dropped from the FILE name but kept in the path.
        var unscoped = packageName;
        var slash = packageName.IndexOf('/');
        if (slash >= 0)
        {
            unscoped = packageName.Substring(slash + 1);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}/{1}/-/{2}-{3}.tgz",
            registry.TrimEnd('/'),
            packageName,
            unscoped,
            version);
    }

    /// <summary>Percent-encode the scope separator, which is what the registry expects for metadata.</summary>
    public static string EncodePackageName(string packageName) => packageName.Replace("/", "%2f");

    /// <summary>
    ///     The published Subresource-Integrity string for a version, from its metadata document.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Hand-scanned rather than parsed with a JSON library, for the same reason the Tailwind
    ///         resolver hand-scans <c>sha256sums.txt</c>: this assembly is loaded into the MSBuild host
    ///         process, and adding an assembly reference there to read one string is a bigger risk than
    ///         the scan. The field is unambiguous — the document has exactly one <c>"integrity"</c>.
    ///     </para>
    ///     <para>
    ///         Returns null when the document does not carry one, which the caller reports as a failure
    ///         rather than treating as nothing-to-check. An unverifiable download of a file about to be
    ///         executed by the build is not a download worth keeping.
    ///     </para>
    /// </remarks>
    public static string? ExpectedIntegrity(string? versionDocument)
    {
        if (string.IsNullOrEmpty(versionDocument))
        {
            return null;
        }

        const string Key = "\"integrity\"";
        var at = versionDocument!.IndexOf(Key, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var colon = versionDocument.IndexOf(':', at + Key.Length);
        if (colon < 0)
        {
            return null;
        }

        var open = versionDocument.IndexOf('"', colon + 1);
        if (open < 0)
        {
            return null;
        }

        var close = versionDocument.IndexOf('"', open + 1);
        if (close < 0)
        {
            return null;
        }

        var value = versionDocument.Substring(open + 1, close - open - 1);

        // Only SHA-512 is accepted. The registry also publishes a legacy SHA-1 "shasum", and quietly
        // falling back to it would weaken the check to one that is no longer collision-resistant while
        // still reporting a verified download.
        return value.StartsWith("sha512-", StringComparison.Ordinal) ? value : null;
    }

    /// <summary>Where a given tool and version live once fetched.</summary>
    /// <remarks>
    ///     Keyed by tool, version and platform so two projects on different pins coexist, and so an
    ///     upgrade is a fresh directory rather than an overwrite of files another build may be executing.
    /// </remarks>
    public static string CacheDirectory(string cacheRoot, TypeScriptTool tool, string version, string packageName)
    {
        var leaf = packageName.Replace("@", string.Empty).Replace("/", "-");
        return Path.Combine(cacheRoot, tool.ToString().ToLowerInvariant(), version, leaf);
    }

    /// <summary>
    ///     The default cache root: beside the user's other Rask tooling, not inside the project.
    /// </summary>
    /// <remarks>
    ///     Outside the repository on purpose, exactly as the Tailwind cache is. tsgo alone unpacks to
    ///     ~27 MB; in <c>obj/</c> that is re-downloaded by every clean, and committed by somebody
    ///     eventually. One per user, shared by every project, is paid for once.
    /// </remarks>
    public static string DefaultCacheRoot(string homeDirectory) =>
        Path.Combine(homeDirectory, ".rask", "typescript");

    /// <summary>This machine's OS, or null on something none of these tools target.</summary>
    public static ToolOs? CurrentOs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return ToolOs.MacOs;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ToolOs.Windows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return ToolOs.Linux;
        }

        return null;
    }
}
