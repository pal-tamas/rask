using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Cli.Scaffolding;

namespace Rask.Cli;

/// <summary>
///     What <c>rask dev</c> discovered about the project it is about to run: which project file, what kind
///     of app, and what the launch profile says. Pure over <see cref="IFileSystem" /> so it is fully
///     unit-testable without a project on disk.
/// </summary>
internal sealed partial record DevTarget(
    DevTemplateKind Kind,
    string ProjectPath,
    string ProjectDirectory,
    string? LaunchUrl,
    bool ProfileLaunchesBrowser)
{
    /// <summary>
    ///     Whether this project has islands worth running a Vite dev server for.
    /// </summary>
    /// <remarks>
    ///     Orthogonal to <see cref="Kind" />: islands live in the HOST project, so a plain
    ///     <see cref="DevTemplateKind.Server" /> app can have them.
    /// </remarks>
    public bool HasIslands { get; init; }

    /// <summary>
    ///     Where the island dev server will listen. Null when the project has no islands.
    /// </summary>
    /// <remarks>
    ///     Read from the csproj so an app that moved the port keeps working, and defaulted to 5174 —
    ///     not Vite's 5173, which an app's own Vite project may already hold.
    /// </remarks>
    public string? IslandDevServerUrl { get; init; }

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
        var kind = Classify(fileSystem, csproj);

        // Once. It walks the project tree, and the tree it walks contains node_modules — which for a
        // project with islands is tens of thousands of files. Calling it from two initialisers walked
        // it twice on every `rask dev` startup.
        var islands = HasIslandSources(fileSystem, directory);

        return new DevTarget(kind, resolved, directory, url, launchesBrowser)
        {
            HasIslands = islands,
            IslandDevServerUrl = islands ? ReadIslandDevServerUrl(fileSystem, csproj) : null,
        };
    }

    /// <summary>
    ///     Whether the project holds an island front end the bundler would build.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A cheaper approximation of the discovery globs in <c>Rask.External.targets</c>, and it only
    ///         has to be right about WHETHER to start a dev server — the build decides what is actually an
    ///         island. Erring towards yes costs a Vite process that serves nothing; erring towards no
    ///         costs the hot reload this exists for.
    ///     </para>
    ///     <para>
    ///         The <c>package.json</c> check comes first and is the same gate the targets use: without one
    ///         the bundler cannot run at all, so there is nothing to serve however many <c>.tsx</c> files
    ///         are lying around.
    ///     </para>
    /// </remarks>
    private static readonly string[] IslandSourcePatterns = ["*.tsx", "*.jsx", "*.vue", "*.svelte"];

    private static bool HasIslandSources(IFileSystem fileSystem, string projectDirectory)
    {
        if (!fileSystem.FileExists(Path.Combine(projectDirectory, "package.json")))
        {
            return false;
        }

        try
        {
            if (IslandSourcePatterns
                .SelectMany(pattern => fileSystem.ListFilesRecursive(projectDirectory, pattern))
                .Any(file => !IsBuildOutput(projectDirectory, file)))
            {
                return true;
            }

            // A .ts counts only beside a .cs of the same name — the Lit and Angular pairing rule.
            // Without that filter every piece of scoped TypeScript in the project would start a dev
            // server.
            foreach (var file in fileSystem.ListFilesRecursive(projectDirectory, "*.ts"))
            {
                if (IsBuildOutput(projectDirectory, file)
                    || file.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (fileSystem.FileExists(Path.ChangeExtension(file, ".cs")))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // One unreadable directory anywhere under the project must not stop `rask dev` from
            // running the app. Losing hot reload for the islands is the right failure here; refusing
            // to start is not.
            return false;
        }

        return false;
    }

    /// <summary>Where the island dev server listens, as the csproj set it or 5174 by default.</summary>
    private static string ReadIslandDevServerUrl(IFileSystem fileSystem, string csproj)
    {
        var text = ReadOrEmpty(fileSystem, csproj);

        var explicitUrl = ExternalDevServerUrlProperty().Match(text);
        if (explicitUrl.Success)
        {
            return explicitUrl.Groups["value"].Value;
        }

        var port = ExternalDevServerPortProperty().Match(text);

        return "http://localhost:" + (port.Success ? port.Groups["value"].Value : "5174");
    }

    /// <summary>Whether a discovered file is build output rather than someone's source.</summary>
    private static bool IsBuildOutput(string projectDirectory, string file)
    {
        var relative = Path.GetRelativePath(projectDirectory, file)
            .Replace(Path.DirectorySeparatorChar, '/');

        return relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
               || relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
               || relative.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase)
               || relative.Contains("node_modules/", StringComparison.OrdinalIgnoreCase);
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
            if (string.Equals(parent, directory, StringComparison.Ordinal))
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
            // Two shapes of WebAssembly client: a referenced Rask WASM project, and the one-project build
            // whose browser half lives in Client/.
            if (ReferencesWasmProject(fileSystem, csproj, text) || HasOneProjectClient(fileSystem, csproj, text))
            {
                return DevTemplateKind.WasmHosted;
            }

            // A host naming a sibling .Client project, whose csproj the probe above could not read — a
            // reference to a project not on disk yet, say. The referenced projects were already read, so
            // this is only the naming convention's last word.
            return text.Contains(".Client", StringComparison.Ordinal)
                ? DevTemplateKind.WasmHosted
                : DevTemplateKind.Server;
        }

        return DevTemplateKind.Unknown;
    }

    /// <summary>
    ///     The one-project build: a <c>Client/Program.cs</c> beside the project file is the browser half's
    ///     entry point — the convention the build keys on — and <c>&lt;RaskClient&gt;false&lt;/RaskClient&gt;</c>
    ///     turns it off in both places.
    /// </summary>
    private static bool HasOneProjectClient(IFileSystem fileSystem, string csprojPath, string text)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(csprojPath));
        return directory is not null
               && fileSystem.FileExists(Path.Combine(directory, "Client", "Program.cs"))
               && !text.Contains("<RaskClient>false</RaskClient>", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Does any <c>ProjectReference</c> point at a WASM client? Reads each referenced csproj and looks
    ///     for the WebAssembly SDK or Rask's own <c>&lt;RaskWasm&gt;</c> marker — the same marker
    ///     <c>Rask.Spa.Hosting.targets</c> probes for at build time, so the CLI and the build agree on
    ///     what a wasm-hosted solution is.
    /// </summary>
    private static bool ReferencesWasmProject(IFileSystem fileSystem, string csprojPath, string text)
    {
        var hostDirectory = Path.GetDirectoryName(Path.GetFullPath(csprojPath));
        if (hostDirectory is null)
        {
            return false;
        }

        foreach (Match match in ProjectReferenceInclude().Matches(text))
        {
            var relative = match.Groups["value"].Value.Replace('\\', Path.DirectorySeparatorChar);
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

            foreach (var profile in profiles.EnumerateObject().Select(property => property.Value))
            {
                if (profile.ValueKind != JsonValueKind.Object
                    || !profile.TryGetProperty("commandName", out var command)
                    || command.ValueKind != JsonValueKind.String
                    || !string.Equals(command.GetString(), "Project", StringComparison.Ordinal))
                {
                    continue;
                }

                var launches = profile.TryGetProperty("launchBrowser", out var lb)
                               && lb.ValueKind == JsonValueKind.True;

                var urls = profile.TryGetProperty("applicationUrl", out var au)
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

    [GeneratedRegex(@"<RaskExternalDevServerUrl>\s*(?<value>[^<\s]+)\s*</RaskExternalDevServerUrl>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ExternalDevServerUrlProperty();

    [GeneratedRegex(@"<RaskExternalDevServerPort>\s*(?<value>\d+)\s*</RaskExternalDevServerPort>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ExternalDevServerPortProperty();

    [GeneratedRegex(@"<ProjectReference\s+Include\s*=\s*""(?<value>[^""]+)""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProjectReferenceInclude();
}
