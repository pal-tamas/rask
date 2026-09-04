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

    /// <summary>
    ///     A TypeScript front end on an ASP.NET host. Two processes: the host, and the bundler's own dev
    ///     server.
    /// </summary>
    SpaHosted,

    /// <summary>
    ///     A meta framework — Nuxt, Next, SvelteKit and the rest — on an ASP.NET host. Two processes, like
    ///     <see cref="SpaHosted" />, and the same division of labour: the framework's own dev server owns
    ///     the front end for the session and the browser talks to it.
    /// </summary>
    MetaHosted,

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
    /// <summary>
    ///     The client's directory, for a <see cref="DevTemplateKind.SpaHosted" /> or
    ///     <see cref="DevTemplateKind.MetaHosted" /> app whose client was found. Null everywhere else, and
    ///     null for a host whose client is somewhere non-conventional.
    /// </summary>
    public string? ClientDirectory { get; init; }

    /// <summary>
    ///     Which meta framework the host was built against, for a <see cref="DevTemplateKind.MetaHosted" />
    ///     app. Null everywhere else.
    /// </summary>
    /// <remarks>
    ///     The name from <c>RaskMetaFramework</c>, verbatim — it is what the banner prints and what decides
    ///     the dev server's port, and both are better wrong-and-obvious than silently generic.
    /// </remarks>
    public string? MetaFramework { get; init; }

    /// <summary>Where the client's own dev server listens — Vite's 5173, Angular's 4200, Nuxt's 3000, or
    ///     whatever the host's csproj says. Null when there is no front end.</summary>
    public string? ClientDevServerUrl { get; init; }

    /// <summary>The npm script that starts it: <c>dev</c> for Vite, <c>start</c> for the Angular CLI.</summary>
    public string? ClientDevScript { get; init; }

    /// <summary>
    ///     Whether this project has islands worth running a Vite dev server for.
    /// </summary>
    /// <remarks>
    ///     Orthogonal to <see cref="Kind" />: islands live in the HOST project, so a plain
    ///     <see cref="DevTemplateKind.Server" /> app can have them and a SPA-hosted one can have both a
    ///     client dev server and an island one.
    /// </remarks>
    public bool HasIslands { get; init; }

    /// <summary>
    ///     Where the island dev server will listen. Null when the project has no islands.
    /// </summary>
    /// <remarks>
    ///     Read from the csproj so an app that moved the port keeps working, and defaulted to 5174 —
    ///     NOT Vite's 5173, which is the SPA client's. A solution with both would otherwise have two
    ///     dev servers fighting for one port, and the loser fails in a way that reads as a Rask bug.
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
        var meta = kind == DevTemplateKind.MetaHosted ? ReadMetaFramework(fileSystem, csproj) : null;

        // Both front-end lanes put the app in a `Client` folder inside the host, which is what makes one
        // resolver right for both. The meta lane can move it with RaskMetaAppDir, and that is read here
        // because the app directory is also where its publish output lands — a host that moved it would
        // otherwise get no dev server at all, with nothing said.
        var client = kind switch
        {
            DevTemplateKind.SpaHosted => FrontEndDirectory(fileSystem, resolved, "Client"),
            DevTemplateKind.MetaHosted => FrontEndDirectory(fileSystem, resolved, ReadMetaAppDir(fileSystem, csproj)),
            _ => null,
        };

        // Once. It walks the project tree, and the tree it walks contains node_modules — which for a
        // project with islands is tens of thousands of files. Calling it from two initialisers walked
        // it twice on every `rask dev` startup.
        var islands = HasIslandSources(fileSystem, directory);

        return new DevTarget(kind, resolved, directory, url, launchesBrowser)
        {
            ClientDirectory = client,
            MetaFramework = meta,
            // The meta lane's answer comes from the framework, so it holds even when the app directory was
            // not found — and pointing `--open` at Vite's port because a Nuxt app was moved would be a
            // worse wrong answer than the one it replaces.
            ClientDevServerUrl = meta is not null
                ? MetaDevServerUrl(meta)
                : client is null
                    ? null
                    : ReadDevServerUrl(fileSystem, csproj),
            ClientDevScript = client is null ? null : ReadDevScript(fileSystem, client),
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
    private static bool HasIslandSources(IFileSystem fileSystem, string projectDirectory)
    {
        if (!fileSystem.FileExists(Path.Combine(projectDirectory, "package.json")))
        {
            return false;
        }

        try
        {
            foreach (var pattern in new[] { "*.tsx", "*.jsx", "*.vue", "*.svelte" })
            {
                foreach (var file in fileSystem.ListFilesRecursive(projectDirectory, pattern))
                {
                    if (!IsBuildOutput(projectDirectory, file))
                    {
                        return true;
                    }
                }
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

        var explicitUrl = Regex.Match(
            text, @"<RaskExternalDevServerUrl>\s*([^<\s]+)\s*</RaskExternalDevServerUrl>");
        if (explicitUrl.Success)
        {
            return explicitUrl.Groups[1].Value;
        }

        var port = Regex.Match(
            text, @"<RaskExternalDevServerPort>\s*(\d+)\s*</RaskExternalDevServerPort>");

        return "http://localhost:" + (port.Success ? port.Groups[1].Value : "5174");
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

    /// <summary>
    ///     Where the client's own dev server listens, read from the property the scaffold baked into the
    ///     host's csproj.
    /// </summary>
    /// <remarks>
    ///     Read rather than assumed, because it is not the same for every framework — Vite listens on 5173
    ///     and Angular's <c>ng serve</c> on 4200 — and it is the host that already carries the answer, for
    ///     its own "nothing built yet" page. Vite's default when the property is absent, which is what an
    ///     older scaffold has.
    /// </remarks>
    private static string ReadDevServerUrl(IFileSystem fileSystem, string csproj)
    {
        var match = Regex.Match(
            ReadOrEmpty(fileSystem, csproj),
            @"<RaskSpaDevServerUrl>\s*([^<\s]+)\s*</RaskSpaDevServerUrl>");

        return match.Success ? match.Groups[1].Value : "http://localhost:5173";
    }

    /// <summary>The meta framework the host names in its csproj, or null when it names none.</summary>
    private static string? ReadMetaFramework(IFileSystem fileSystem, string csproj)
    {
        var match = Regex.Match(
            ReadOrEmpty(fileSystem, csproj),
            @"<RaskMetaFramework>\s*([^<\s]+)\s*</RaskMetaFramework>");

        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Where the front end lives, as <c>RaskMetaAppDir</c> set it or <c>Client</c> by default.</summary>
    private static string ReadMetaAppDir(IFileSystem fileSystem, string csproj)
    {
        var match = Regex.Match(
            ReadOrEmpty(fileSystem, csproj),
            @"<RaskMetaAppDir>\s*([^<\s]+)\s*</RaskMetaAppDir>");

        return match.Success ? match.Groups[1].Value : "Client";
    }

    /// <summary>Where the meta framework's own dev server listens.</summary>
    /// <remarks>
    ///     <para>
    ///         Asked of the same table <c>rask new</c> scaffolds from, rather than restated here. The
    ///         scaffold's own next-steps text and the generated README print that value, so a second
    ///         copy would let <c>--open</c> point at one port while the project's instructions name
    ///         another — and nothing would fail.
    ///     </para>
    ///     <para>
    ///         Derived from the framework rather than read from a property, because unlike the SPA lane
    ///         there is no scaffold baking an answer into the csproj: the app is created by the
    ///         framework's own tool, which has a default and is the authority on it.
    ///     </para>
    ///     <para>
    ///         This only decides where <c>--open</c> points; a front end told to listen elsewhere still
    ///         runs, and <c>--urls</c> still wins outright. A framework name this does not recognise —
    ///         a typo in the csproj, which the build itself rejects — gets no answer rather than a
    ///         plausible-looking wrong one.
    ///     </para>
    /// </remarks>
    private static string? MetaDevServerUrl(string framework) =>
        MetaTemplate.TryGet(framework, out var template) ? template.DevServerUrl : null;

    /// <summary>
    ///     The npm script that starts the client's dev server: <c>dev</c> where there is one, otherwise
    ///     <c>start</c>.
    /// </summary>
    /// <remarks>
    ///     Read from the client's own package.json rather than decided per framework, because that file is
    ///     what actually settles it — create-vite writes <c>dev</c>, the Angular CLI writes <c>start</c>,
    ///     and a project that renamed either is still answered correctly.
    /// </remarks>
    private static string ReadDevScript(IFileSystem fileSystem, string clientDirectory)
    {
        try
        {
            var manifest = fileSystem.ReadAllText(Path.Combine(clientDirectory, "package.json"));
            using var document = JsonDocument.Parse(manifest);
            if (document.RootElement.TryGetProperty("scripts", out var scripts))
            {
                if (scripts.TryGetProperty("dev", out _))
                {
                    return "dev";
                }

                if (scripts.TryGetProperty("start", out _))
                {
                    return "start";
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Unreadable or malformed: fall through to the common default rather than refusing to run
            // the host over a file that is only needed for the other half.
        }

        return "dev";
    }

    /// <summary>
    ///     The front end inside a host, by the same convention both builds use: a folder in the project
    ///     directory — <c>Client</c> unless the meta lane moved it — holding a <c>package.json</c>.
    /// </summary>
    /// <remarks>
    ///     The <c>package.json</c> check is what makes this safe, not decoration — and it carries more
    ///     weight than it did when the rule looked at siblings named <c>*.Client</c>, because a folder
    ///     called <c>Client</c> is a far more ordinary thing for a project to contain than a sibling
    ///     project was. A folder called <c>Client</c> that also holds a <c>package.json</c> is not.
    /// </remarks>
    private static string? FrontEndDirectory(IFileSystem fileSystem, string csproj, string appDirectory)
    {
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(csproj));
        if (projectDirectory is null)
        {
            return null;
        }

        var client = Path.Combine(projectDirectory, appDirectory);

        return fileSystem.FileExists(Path.Combine(client, "package.json")) ? client : null;
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
            // Checked before the wasm-hosted shape, because a SPA host also has a sibling named .Client —
            // one holding a package.json rather than a csproj, which is exactly what the Rask.Spa.Hosting
            // targets look for. Keyed on the package reference rather than on that directory: the client
            // may have been moved with RaskSpaClientDir, and the package is what actually decides how the
            // app is served.
            if (text.Contains("Rask.Spa.Hosting", StringComparison.Ordinal))
            {
                return DevTemplateKind.SpaHosted;
            }

            // The other front-end lane, and keyed on the package for the same reason. Without this a meta
            // host was read as a plain Server: no dev server was started beside it, and — the expensive
            // half — RaskMetaBuild stayed true, so every save ran a full Nuxt or Next PRODUCTION build
            // whose output nothing in the session then read.
            if (text.Contains("Rask.Meta.Hosting", StringComparison.Ordinal))
            {
                return DevTemplateKind.MetaHosted;
            }

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
