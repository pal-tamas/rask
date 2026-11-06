using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace KitBundle;

/// <summary>
///     One of Rask.Ui's script engines: a TypeScript entry in <c>src/Rask.Ui/Resources/{name}/</c>, the packages
///     its <c>bundle.json</c> pins and <c>bundle-lock.json</c> locks, and the committed files built from them.
/// </summary>
internal abstract class Engine
{
    /// <summary>English, as a browser's <c>Intl.Collator('en')</c> and <c>localeCompare</c> sort it (ICU's collation).</summary>
    protected static readonly StringComparer English = StringComparer.Create(CultureInfo.GetCultureInfo("en"), ignoreCase: false);

    internal static IReadOnlyList<Engine> All { get; } = [new EditorEngine(), new PhoneEngine()];

    /// <summary><c>editor</c> or <c>phone</c>: the directory under Resources, and the name on the command line.</summary>
    internal abstract string Name { get; }

    /// <summary>The committed bundles, by file name under Resources: what every visitor downloads, so their sizes are a budget.</summary>
    internal abstract IReadOnlyList<string> Bundles { get; }

    /// <summary>A package family that moves only as one (every <c>@tiptap/</c> package at one release), or null.</summary>
    internal virtual string? Family => null;

    internal string Directory => Path.Combine(Repository.Resources, Name);

    internal string ManifestPath => Path.Combine(Directory, "bundle.json");

    // Not bundle.lock.json: under Resources/ a {Family}.{culture}.json is a translation catalog, and "lock" reads as one.
    internal string LockPath => Path.Combine(Directory, "bundle-lock.json");

    /// <summary>The TypeScript entry esbuild bundles, beside bundle.json.</summary>
    protected abstract string Entry { get; }

    internal static Engine Named(string? name) =>
        All.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal))
        ?? throw new KitBundleException($"name an engine: {string.Join(" or ", All.Select(e => e.Name))}");

    /// <summary>
    ///     The release <paramref name="dependency" /> follows, when it is not npm's latest; null for npm's latest.
    /// </summary>
    internal virtual Task<string?> Follows(string dependency) => Task.FromResult<string?>(null);

    /// <summary>Resolves bundle.json against the registry and writes the tree to bundle-lock.json.</summary>
    internal async Task Lock() => BundleLock.Write(LockPath, await TreeResolver.Resolve(BundleManifest.Read(ManifestPath)));

    /// <summary>
    ///     Fetches what a build of <paramref name="locked" /> needs — every tarball, and esbuild — before anything is
    ///     written, so a registry that stops answering halfway leaves the committed files as they were.
    /// </summary>
    internal static async Task Prefetch(IEnumerable<LockedPackage> locked)
    {
        await Esbuild.Ensure();
        await Parallel.ForEachAsync(locked.Where(p => p.InstallsHere), async (package, _) => await Installer.Fetch(package));
    }

    /// <summary>Installs bundle-lock.json, bundles the entry and writes every committed file built from it.</summary>
    internal async Task Build()
    {
        var locked = BundleLock.Read(LockPath);
        var tree = Path.Combine(Repository.Work, Name, "tree");

        // The entry is bundled from beside its node_modules, as npm's tree would have it: esbuild finds a package
        // by walking up from the importing file, and names a module by its path from the working directory.
        await Installer.Install(locked, tree);
        File.Copy(Path.Combine(Directory, Entry), Path.Combine(tree, Entry), overwrite: true);
        await Bundle(tree, locked);
    }

    /// <summary>Bundles <see cref="Entry" /> in <paramref name="tree" /> and writes the engine's committed files.</summary>
    protected abstract Task Bundle(string tree, IReadOnlyList<LockedPackage> locked);

    /// <summary>The esbuild options every engine is built with: one minified ES module, its own notices kept out of it.</summary>
    protected IEnumerable<string> Options(string outfile, string banner) =>
    [
        Entry, "--bundle", "--format=esm", "--target=es2022", "--minify", "--legal-comments=none",
        "--banner:js=" + banner, "--log-level=warning", "--outfile=" + outfile,
    ];

    /// <summary>The short content hash a committed file is versioned by: the first 8 hex digits of its SHA-256.</summary>
    protected static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..8];

    /// <summary>One notice: a ruled title and the licence text.</summary>
    protected static string Section(string title, string text)
    {
        var rule = new string('=', 78);
        return $"\n{rule}\n{title}\n{rule}\n{text.Trim()}\n";
    }

    /// <summary>Rewrites <c>public const string {name} = "…";</c> in a kit source to <paramref name="value" />.</summary>
    protected static string WithConstant(string source, string name, string value) =>
        new Regex($"(public const string {name} = \")[0-9a-f]*(\";)").Replace(source, m => m.Groups[1].Value + value + m.Groups[2].Value, 1);
}
