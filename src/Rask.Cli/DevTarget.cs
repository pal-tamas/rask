using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Cli.Scaffolding;

namespace Rask.Cli;

/// <summary>Which template shape <c>rask dev</c> is looking at, which decides how (and whether) to run it.</summary>
internal enum DevTemplateKind
{
    /// <summary>An ASP.NET host — <c>rask new</c>'s default.</summary>
    Server,

    /// <summary>A wasm-hosted solution; the project to run is the <c>.Server</c> host, not the client.</summary>
    WasmHosted,

    /// <summary>A standalone WebAssembly app: no ASP.NET host, and no launch profile scaffolded.</summary>
    WasmStandalone,

    /// <summary>Something else. Treated like <see cref="Server" />, minus the banner URL.</summary>
    Unknown
}

/// <summary>
///     What <c>rask dev</c> discovered about the project it is about to run: which project file, what kind
///     of app, and what the launch profile says. Pure over <see cref="IFileSystem" /> so it is fully
///     unit-testable without a project on disk.
/// </summary>
internal sealed record DevTarget(
    DevTemplateKind Kind,
    string ProjectPath,
    string ProjectDirectory,
    string? LaunchUrl,
    bool ProfileLaunchesBrowser)
{
    /// <summary>The project name, used in the banner.</summary>
    public string Name => Path.GetFileNameWithoutExtension(ProjectPath);

    /// <summary>
    ///     Resolves the project to run. <paramref name="explicitProject" /> (from <c>--project</c>) wins and
    ///     may be either a <c>.csproj</c> path or a directory; otherwise this walks up to the nearest single
    ///     project, exactly as <c>rask db</c> does. Returns null when nothing could be resolved — the caller
    ///     reports it.
    /// </summary>
    public static DevTarget? Detect(IFileSystem fileSystem, string workingDirectory, string? explicitProject)
    {
        var csproj = explicitProject is { Length: > 0 }
            ? ResolveCsproj(fileSystem, explicitProject)
            : LocateCsproj(fileSystem, workingDirectory);

        if (csproj is null)
        {
            return null;
        }

        // Resolve symlinks, not just `..` and separators. Handing `dotnet watch` a project path that
        // traverses a symlink makes it compute an EMPTY hot-reload delta — the edit is seen, the workspace
        // document is updated, and then nothing is applied and nothing is reported. On macOS this is the
        // default for anything under the temp directory (/var → /private/var). See RealPath.
        var resolved = RealPath.Resolve(csproj);
        var directory = Path.GetDirectoryName(resolved) ?? workingDirectory;
        var (url, launchesBrowser) = ReadLaunchProfile(fileSystem, directory);
        return new DevTarget(Classify(fileSystem, csproj), resolved, directory, url, launchesBrowser);
    }

    private static string? ResolveCsproj(IFileSystem fileSystem, string projectPathOrDirectory)
    {
        if (projectPathOrDirectory.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return fileSystem.FileExists(projectPathOrDirectory) ? projectPathOrDirectory : null;
        }

        var projects = SafeListFiles(fileSystem, projectPathOrDirectory, "*.csproj");
        return projects.Count == 1 ? projects[0] : null;
    }

    private static string? LocateCsproj(IFileSystem fileSystem, string workingDirectory)
    {
        var directory = Path.GetFullPath(workingDirectory);
        while (!string.IsNullOrEmpty(directory))
        {
            var projects = SafeListFiles(fileSystem, directory, "*.csproj");
            if (projects.Count == 1)
            {
                return projects[0];
            }

            if (projects.Count > 1)
            {
                return null; // Ambiguous — the caller asks for --project.
            }

            // A wasm-hosted solution is a directory OF projects: {name}.Client/, .Server/, .Shared/, with
            // no csproj at the root. Running it means running the Server host, which the next-steps text
            // used to make the user type by hand. Pick it when it is unambiguous.
            var server = ServerProjectOneLevelDown(fileSystem, directory);
            if (server is not null)
            {
                return server;
            }

            var parent = Path.GetDirectoryName(directory);
            if (parent == directory)
            {
                break;
            }

            directory = parent!;
        }

        return null;
    }

    private static string? ServerProjectOneLevelDown(IFileSystem fileSystem, string directory)
    {
        var candidates = SafeListFiles(fileSystem, directory, "*.csproj", recursive: true)
            .Where(p => IsOneLevelBelow(directory, p))
            .Where(p => p.EndsWith(".Server.csproj", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static bool IsOneLevelBelow(string root, string file)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(file));
        return parent is not null
               && string.Equals(Path.GetDirectoryName(parent), Path.GetFullPath(root), StringComparison.Ordinal);
    }

    private static DevTemplateKind Classify(IFileSystem fileSystem, string csproj)
    {
        string text;
        try
        {
            text = fileSystem.ReadAllText(csproj);
        }
        catch (IOException)
        {
            return DevTemplateKind.Unknown;
        }

        // Order matters: the wasm-hosted client uses the WebAssembly SDK just like a standalone one.
        if (text.Contains("Microsoft.NET.Sdk.WebAssembly", StringComparison.Ordinal))
        {
            return DevTemplateKind.WasmStandalone;
        }

        if (text.Contains("Microsoft.NET.Sdk.Web", StringComparison.Ordinal))
        {
            // A Server host that references a sibling .Client project is the wasm-hosted shape. The
            // name check is what `rask new` produces, and it costs no I/O — but it is only a naming
            // convention, so fall back to actually reading the referenced projects. Without that,
            // a host whose client is not called *.Client (the repo's own Rask.Example.Wasm.Host among
            // them) is misread as a plain Server and never gets the WASM dev bundle.
            return text.Contains(".Client", StringComparison.Ordinal) || ReferencesWasmProject(fileSystem, csproj, text)
                ? DevTemplateKind.WasmHosted
                : DevTemplateKind.Server;
        }

        return DevTemplateKind.Unknown;
    }

    /// <summary>
    ///     Does any <c>ProjectReference</c> point at a WASM client? Reads each referenced csproj and looks
    ///     for the WebAssembly SDK or Rask's own <c>&lt;RaskWasm&gt;</c> marker — the same marker
    ///     <c>Rask.Wasm.Hosting.targets</c> probes for at build time, so the CLI and the build agree on
    ///     what a wasm-hosted solution is.
    /// </summary>
    private static bool ReferencesWasmProject(IFileSystem fileSystem, string csprojPath, string text)
    {
        var hostDirectory = Path.GetDirectoryName(Path.GetFullPath(csprojPath));
        if (hostDirectory is null)
        {
            return false;
        }

        foreach (Match match in Regex.Matches(text, @"<ProjectReference\s+Include\s*=\s*""([^""]+)"""))
        {
            var relative = match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
            var referenced = Path.GetFullPath(Path.Combine(hostDirectory, relative));
            if (!fileSystem.FileExists(referenced))
            {
                continue;
            }

            var referencedText = ReadOrEmpty(fileSystem, referenced);
            if (referencedText.Contains("Microsoft.NET.Sdk.WebAssembly", StringComparison.Ordinal)
                || referencedText.Contains("<RaskWasm>true</RaskWasm>", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadOrEmpty(IFileSystem fileSystem, string path)
    {
        try
        {
            return fileSystem.ReadAllText(path);
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    ///     Reads <c>Properties/launchSettings.json</c> for the first <c>commandName: Project</c> profile,
    ///     preferring its <c>https://</c> URL. Never throws — a malformed or absent file simply means we
    ///     have no URL to show, which is not worth failing the command over (mirrors how the generate/deploy
    ///     configs treat a corrupt file).
    /// </summary>
    private static (string? Url, bool LaunchesBrowser) ReadLaunchProfile(IFileSystem fileSystem, string projectDirectory)
    {
        var path = Path.Combine(projectDirectory, "Properties", "launchSettings.json");
        if (!fileSystem.FileExists(path))
        {
            return (null, false);
        }

        try
        {
            using var doc = JsonDocument.Parse(fileSystem.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("profiles", out var profiles)
                || profiles.ValueKind != JsonValueKind.Object)
            {
                return (null, false);
            }

            foreach (var profile in profiles.EnumerateObject())
            {
                if (profile.Value.ValueKind != JsonValueKind.Object
                    || !profile.Value.TryGetProperty("commandName", out var command)
                    || command.ValueKind != JsonValueKind.String
                    || !string.Equals(command.GetString(), "Project", StringComparison.Ordinal))
                {
                    continue;
                }

                var launches = profile.Value.TryGetProperty("launchBrowser", out var lb)
                               && lb.ValueKind == JsonValueKind.True;

                var urls = profile.Value.TryGetProperty("applicationUrl", out var au)
                           && au.ValueKind == JsonValueKind.String
                    ? au.GetString()
                    : null;

                return (PreferHttps(urls), launches);
            }
        }
        catch (JsonException)
        {
            // Malformed launchSettings — carry on without a URL.
        }
        catch (IOException)
        {
            // Unreadable — same.
        }

        return (null, false);
    }

    private static string? PreferHttps(string? applicationUrl)
    {
        if (string.IsNullOrWhiteSpace(applicationUrl))
        {
            return null;
        }

        var urls = applicationUrl
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return urls.FirstOrDefault(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
               ?? urls.FirstOrDefault();
    }

    private static IReadOnlyList<string> SafeListFiles(
        IFileSystem fileSystem, string directory, string pattern, bool recursive = false)
    {
        try
        {
            return recursive
                ? fileSystem.ListFilesRecursive(directory, pattern)
                : fileSystem.ListFiles(directory, pattern);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
