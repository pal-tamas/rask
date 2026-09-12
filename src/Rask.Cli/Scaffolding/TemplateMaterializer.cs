using System.Collections.Frozen;
using System.Text;
using System.Text.Json;

namespace Rask.Cli.Scaffolding;

/// <summary>
///     Turns a committed template tree into the files a scaffold writes.
/// </summary>
/// <remarks>
///     <para>
///         The tree under <c>src/Rask.Templates/&lt;key&gt;/</c> IS the output: editing a file there
///         changes what <c>rask new</c> writes, byte for byte, because there is no second copy of the
///         content anywhere. Three things are applied on the way out, and nothing else:
///     </para>
///     <list type="number">
///         <item>battery conditionals — whole files named in <c>template.json</c>, and regions marked
///         inside files (see <see cref="TemplateMarkers"/>);</item>
///         <item>the name token, <c>Company.RaskServer</c>, in content AND in paths, which is the
///         mechanism the hand-written generators already used;</item>
///         <item><c>{{RaskVersion}}</c>, the version of the Rask packages the scaffold should pin.</item>
///     </list>
///     <para>
///         What it deliberately does NOT do is run anybody else's scaffolder. The front-end templates
///         used to shell out to <c>create-vite@latest</c>, <c>nuxi@latest</c> and the rest, which meant
///         `rask new` needed the network and a Node install, produced a different tree every morning,
///         and left every front-end dependency invisible to this repository — no committed manifest, so
///         nothing to review and nothing for Dependabot to bump. Owning the trees fixes all three; what
///         it costs is a deliberate refresh (scripts/refresh-templates.sh) when a creator moves on.
///     </para>
/// </remarks>
internal static class TemplateMaterializer
{
    /// <summary>The placeholder every template is written against; rewritten to the app's name.</summary>
    internal const string NameToken = "Company.RaskServer";

    /// <summary>The placeholder for the Rask package version the scaffold pins.</summary>
    internal const string VersionToken = "{{RaskVersion}}";

    /// <summary>
    ///     The npm-safe spelling of <see cref="NameToken"/>, which some tooling writes instead of the
    ///     name itself.
    /// </summary>
    /// <remarks>
    ///     Angular is the one that needs it: <c>ng new</c> derives its project name from the directory
    ///     and lowercases it, so the Angular template says <c>company-raskserver-client</c> in
    ///     angular.json, in the client's package.json name, in a spec's expected text, and in the host's
    ///     RaskSpaDistDir. Substituting only the exact name token leaves every one of those naming the
    ///     placeholder, and the host then looks for the bundle under a directory Angular never writes —
    ///     a scaffolded Angular app that builds and serves nothing.
    /// </remarks>
    internal const string SlugToken = "company-raskserver";

    /// <summary>The manifest, at the root of every template tree.</summary>
    internal const string ManifestFile = "template.json";

    /// <summary>
    ///     Files stored without their leading dot, because the VCS would otherwise read them as rules
    ///     for THIS repository: a nested <c>.gitignore</c> beats the root one, and angular's
    ///     <c>.vscode/</c> line silently drops three files the template is supposed to ship.
    /// </summary>
    private static readonly FrozenDictionary<string, string> InertNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gitignore"] = ".gitignore",
            ["gitattributes"] = ".gitattributes",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    ///     Materialises <paramref name="templateKey"/>'s tree into <paramref name="targetDirectory"/>.
    /// </summary>
    public static IReadOnlyList<ScaffoldFile> Files(
        string targetDirectory,
        string templateKey,
        string name,
        ServerBatteries batteries,
        string version,
        DotnetTarget dotnet,
        IReadOnlyList<string>? islands = null,
        bool vsCode = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetDirectory);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(batteries);
        ArgumentNullException.ThrowIfNull(dotnet);

        var assets = TemplateAssets.Load(templateKey);
        var owners = ReadOwners(assets, templateKey);

        // The island flags are conditions like any other, so the templates can mark their package
        // references with them rather than having those spliced in afterwards.
        var on = islands is { Count: > 0 }
            ? FlagsOn(batteries).Concat(IslandAssembly.Flags(islands)).ToFrozenSet(StringComparer.Ordinal)
            : FlagsOn(batteries);

        var files = new List<ScaffoldFile>(assets.Count);
        foreach (var asset in assets)
        {
            if (string.Equals(asset.Path, ManifestFile, StringComparison.Ordinal))
            {
                continue;
            }

            if (owners.TryGetValue(asset.Path, out var required) && !Satisfied(required, on))
            {
                continue;
            }

            var relative = Rename(asset.Path).Replace(NameToken, name, StringComparison.Ordinal);
            var destination = Path.Combine(targetDirectory, relative.Replace('/', Path.DirectorySeparatorChar));

            if (asset.IsBinary)
            {
                files.Add(new ScaffoldFile(destination, string.Empty) { Bytes = asset.Bytes });
                continue;
            }

            var text = Decode(asset.Bytes);
            text = TemplateMarkers.Apply(text, on, asset.Path);
            text = text
                .Replace(VersionToken, version, StringComparison.Ordinal)
                // Before the name token, because the slug is the name's own lower-case spelling and
                // replacing the name first would leave nothing for this to match.
                .Replace(SlugToken, Slug(name), StringComparison.Ordinal)
                .Replace(NameToken, name, StringComparison.Ordinal);

            files.Add(new ScaffoldFile(destination, text));
        }

        var written = islands is { Count: > 0 }
            ? IslandAssembly.Apply(targetDirectory, islands, name, files)
            : files;

        // The .vscode fragment tree is assembled HERE, next to the islands, for the reason the rewrite
        // below exists: it is one more set of files added to this list, and anything added after the
        // rewrite is a file the rewrite never saw. It carried net10.0 in launch.json's program path, so
        // F5 on a net11.0 scaffold started a dll that was never built.
        if (vsCode)
        {
            written = VsCodeAssembly.Apply(targetDirectory, name, written);
        }

        // Last, and over the ASSEMBLED list rather than each asset as it is read: both assemblers above
        // contribute files of their own, and a rewrite inside the loop reached none of them. A no-op for
        // the default target, so a plain `rask new` still writes the committed trees byte for byte.
        written = [.. written.Select(file =>
            file.Bytes is null ? file with { Content = dotnet.Rewrite(file.Content) } : file)];

        VerifyFrameworkRewritten(written, dotnet);
        return written;
    }

    /// <summary>
    ///     Every csproj and Dockerfile a scaffold writes names the .NET version that was asked for.
    /// </summary>
    /// <remarks>
    ///     The rewrite is by exact literal (<see cref="DotnetTarget.Rewrite" />), so a template that spells its
    ///     framework some other way — an attribute instead of an element, a Docker tag written differently, a
    ///     file an assembler adds after the loop — is simply not rewritten, and says nothing. What reaches the
    ///     user is then a project whose csproj and container disagree about the .NET version: `dotnet run`
    ///     works and `docker build` fails on a restore error that names neither. Checked rather than trusted,
    ///     and only when a version other than the default was asked for.
    /// </remarks>
    private static void VerifyFrameworkRewritten(IReadOnlyList<ScaffoldFile> files, DotnetTarget dotnet)
    {
        if (dotnet == DotnetTarget.Default)
        {
            return;
        }

        // Every text file, not just csproj and Dockerfile. The spellings StillNamesTheDefault looks for
        // are exact enough that prose mentioning net10.0 does not match one, and narrowing this to the
        // two obvious file types is what let .vscode/launch.json through.
        var missed = files
            .Where(file => file.Bytes is null)
            .Where(file => dotnet.StillNamesTheDefault(file.Content))
            .Select(file => Path.GetFileName(file.Path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (missed.Length > 0)
        {
            throw new InvalidOperationException(
                $"--framework {dotnet.Moniker} did not reach {string.Join(", ", missed)}: the template names "
                + $"{DotnetTarget.Default.Moniker} in a spelling DotnetTarget.Rewrite does not match. Scaffolding "
                + "would have produced a project whose csproj and Dockerfile disagree about the .NET version.");
        }
    }

    /// <summary>Whether a committed tree exists for <paramref name="templateKey"/>.</summary>
    public static bool Has(string templateKey) => TemplateAssets.Has(templateKey);

    /// <summary>The battery flags that are on, under the names the templates mark regions with.</summary>
    internal static FrozenSet<string> FlagsOn(ServerBatteries batteries)
    {
        ArgumentNullException.ThrowIfNull(batteries);

        var on = new List<string>(15);
        Add(batteries.Pwa, "pwa");
        Add(batteries.Cqrs, "cqrs");
        Add(batteries.Data, "data");
        Add(batteries.Docker, "docker");
        Add(batteries.Jobs, "jobs");
        Add(batteries.Mail, "mail");
        Add(batteries.Cache, "cache");
        Add(batteries.Storage, "storage");
        Add(batteries.Outbox, "outbox");
        Add(batteries.Push, "push");
        Add(batteries.Snapshots, "snapshots");
        Add(batteries.Logs, "logs");
        Add(batteries.Ops, "ops");
        Add(batteries.Wasm, "wasm");

        // Not a `rask new` flag — it is whether the template ships the language registration at all
        // (TemplateInfo.ShipsLocalization, true for the server template and false in the browser, where
        // naming a culture costs about a megabyte of ICU). It is still a CONDITION, because a caller
        // that turns it off must not be handed a string catalog it never asked for.
        Add(batteries.Localization, "localization");
        return on.ToFrozenSet(StringComparer.Ordinal);

        void Add(bool enabled, string flag)
        {
            if (enabled)
            {
                on.Add(flag);
            }
        }
    }

    /// <summary>
    ///     An app name as npm and the Angular CLI spell it: lower case, with every run of characters
    ///     that is not a letter or digit collapsed to a single dash.
    /// </summary>
    /// <remarks>
    ///     Matches what <c>ng new</c> does to a directory name, which is what the Angular template's
    ///     committed files were written against — <c>Company.RaskServer</c> becomes
    ///     <c>company-raskserver</c>. An npm package name may not contain an upper-case letter or a dot,
    ///     so this is a requirement rather than a convention.
    /// </remarks>
    internal static string Slug(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var slug = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                slug.Append(char.ToLowerInvariant(c));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        return slug.ToString().Trim('-');
    }

    /// <summary>The stored name with its leading dot restored, if it was stored without one.</summary>
    private static string Rename(string path)
    {
        var slash = path.LastIndexOf('/');
        var fileName = slash < 0 ? path : path[(slash + 1)..];
        if (!InertNames.TryGetValue(fileName, out var real))
        {
            return path;
        }

        return slash < 0 ? real : string.Concat(path.AsSpan(0, slash + 1), real);
    }

    /// <summary>
    ///     Whether every condition a file names holds: <c>flag</c> needs the flag on, <c>!flag</c> needs
    ///     it off.
    /// </summary>
    /// <remarks>
    ///     The negation exists for files a battery REPLACES rather than adds. Under <c>--wasm</c> the pages
    ///     move into <c>Client/</c>, so the server's own copies of them must not be written at all — and a
    ///     region marker cannot say that about a whole file without leaving an empty one behind.
    /// </remarks>
    internal static bool Satisfied(IEnumerable<string> conditions, IReadOnlySet<string> on)
    {
        foreach (var condition in conditions)
        {
            var negated = condition.StartsWith('!');
            var flag = negated ? condition[1..] : condition;
            if (on.Contains(flag) == negated)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Which flags each conditional file needs, from <c>template.json</c>. Read with
    ///     <see cref="JsonDocument"/> rather than deserialised into a type: it is a handful of string
    ///     arrays, and staying off the reflection-based serialiser keeps the tool trimmable.
    /// </summary>
    private static FrozenDictionary<string, string[]> ReadOwners(
        IReadOnlyList<TemplateAsset> assets, string templateKey)
    {
        var manifest = assets.FirstOrDefault(a =>
            string.Equals(a.Path, ManifestFile, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Template '{templateKey}' has no {ManifestFile}. Every tree needs one: it records which "
                + "files a battery owns wholesale, which is the half of the conditionals that cannot be "
                + "expressed as a comment inside the file.");

        using var document = JsonDocument.Parse(manifest.Bytes);
        if (!document.RootElement.TryGetProperty("files", out var owned))
        {
            return FrozenDictionary<string, string[]>.Empty;
        }

        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var entry in owned.EnumerateObject())
        {
            result[entry.Name] = [.. entry.Value.EnumerateArray().Select(f => f.GetString() ?? "")];
        }

        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    ///     Decodes a template file as UTF-8, dropping the byte-order mark the creators' own tooling
    ///     sometimes writes. Left in, it would be re-emitted into the middle of a scaffolded file.
    /// </summary>
    private static string Decode(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }
}
