using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rask.Cli.Scaffolding;

// The TypeScript front-end templates: an ASP.NET host that answers CQRS over JSON and serves the bundle,
// beside a client the framework's OWN scaffolder produces. Rask overlays four files onto it and patches
// two — everything else is whatever `create-vite` ships today, which is the point.
internal static partial class ProjectGenerator
{
    /// <summary>
    ///     Generates a TypeScript-front-end solution: <c>{name}.Server</c> (ASP.NET + CQRS), and
    ///     <c>{name}.Client</c> scaffolded by the framework's own tool and then overlaid.
    /// </summary>
    /// <remarks>
    ///     Two projects, not three. The wasm-hosted template needs a <c>.Shared</c> because both halves are
    ///     C# and must compile the same record; here the client's half of every contract is generated
    ///     TypeScript, so the messages live in the Server and there is nothing for a third project to hold.
    /// </remarks>
    public static ScaffoldResult GenerateSpa(
        string targetDirectory,
        string name,
        SpaFramework framework,
        ServerBatteries requested,
        string version)
    {
        var batteries = requested.Normalized() with { Cqrs = true };

        var files = new List<(string Path, string Content)>
        {
            ($"{NameToken}.Server/{NameToken}.Server.csproj", SpaServerCsproj(batteries, framework, name, version)),
            ($"{NameToken}.Server/Program.cs", SpaServerProgram(batteries)),
            ($"{NameToken}.Server/Features/Hello/Messages.cs", SpaMessages),
            ($"{NameToken}.Server/Features/Hello/HelloHandlers.cs", SpaHandlers),
            ($"{NameToken}.Server/Properties/launchSettings.json", SpaLaunchSettings),
            ($"{NameToken}.Server/appsettings.json", AppSettings),
            ($"{NameToken}.Server/appsettings.Production.json", AppSettingsProduction),

            ("README.md", SpaReadme(framework)),
        };

        // The overlay: everything else in the client is its own scaffolder's. Angular declares its dev
        // proxy in angular.json instead, so it gets no vite.config.ts — writing one would be a file
        // nothing reads.
        if (framework.WritesViteConfig)
        {
            files.Add(($"{NameToken}.Client/vite.config.ts", SpaViteConfig(framework, batteries.Tailwind)));
        }

        foreach (var (path, content) in framework.ClientFiles)
        {
            files.Add(($"{NameToken}.Client/{path}", content));
        }

        if (batteries.Tailwind)
        {
            // Replaces the scaffolder's demo stylesheet rather than sitting beside it: create-vite's
            // starter CSS styles the placeholder page we have already overlaid away, and leaving it in
            // would fight Tailwind's own reset.
            files.Add(($"{NameToken}.Client/{framework.GlobalStylesheet}", SpaTailwindCss));
        }

        if (batteries.Data)
        {
            files.Add(($"{NameToken}.Server/Features/Shared/AppDbContext.cs", WasmHostedServerDbContext(batteries)));
        }

        if (batteries.Docker)
        {
            files.Add(("Dockerfile", SpaDockerfile));
            files.Add((".dockerignore", DockerIgnore));
        }

        files.AddRange(ProjectHygiene($"{NameToken}.Server/{NameToken}.Server.csproj"));

        var scaffoldFiles = Materialize(targetDirectory, name, files);
        var client = System.IO.Path.Combine(targetDirectory, name + ".Client");

        return new ScaffoldResult(scaffoldFiles, SpaNextSteps(name, framework, batteries.Docker))
        {
            Packages = ["Rask.Cqrs", "Rask.Cqrs.Server", "Rask.Spa.Hosting"],
            RestoreTarget = $"{name}.slnx",
            ExternalScaffolds =
            [
                new ExternalScaffold(
                    "npx",
                    framework.Scaffolder(name),
                    $"Scaffolding the {framework.DisplayName} client with {framework.ScaffolderName}…",
                    "Install Node.js 20.19 or newer from https://nodejs.org "
                    + "(macOS: brew install node; Windows: winget install OpenJS.NodeJS.LTS; "
                    + "Linux: your distro's nodejs package)."),
            ],
            Patches = SpaPatches(client, framework, batteries.Tailwind),
        };
    }

    /// <summary>The edits made to what the client's own scaffolder wrote.</summary>
    private static IReadOnlyList<ScaffoldPatch> SpaPatches(
        string client, SpaFramework framework, bool tailwind)
    {
        var patches = new List<ScaffoldPatch>
        {
            new(
                System.IO.Path.Combine(client, "package.json"),
                json => AddClientDependencies(json, framework, tailwind),
                "adding " + Dependencies(framework)),
            new(
                System.IO.Path.Combine(client, ".gitignore"),
                IgnoreGeneratedContracts,
                "ignoring the generated contracts"),
        };

        if (!framework.WritesViteConfig)
        {
            // Angular's dev proxy is a file plus a pointer at it, and the pointer lives in a config the
            // CLI owns. Patched rather than replaced for the same reason as package.json: angular.json
            // carries the whole build configuration, and overwriting it would mean shipping a copy of it.
            patches.Add(new ScaffoldPatch(
                System.IO.Path.Combine(client, "angular.json"),
                UseProxyConfig,
                "pointing the dev server at proxy.conf.json"));
        }

        return patches;
    }

    /// <summary>
    ///     Points Angular's dev server at the proxy file, wherever that project happens to be named.
    /// </summary>
    /// <remarks>
    ///     Written into <c>angular.json</c> rather than onto the <c>start</c> script, so
    ///     <c>ng serve</c> picks it up however it is launched — including from an IDE, which does not run
    ///     the npm script. Idempotent, and a no-op on a file shaped in a way this does not recognise:
    ///     failing the scaffold over a proxy line would be worse than saying it did not happen.
    /// </remarks>
    internal static string UseProxyConfig(string angularJson)
    {
        var root = JsonNode.Parse(angularJson) as JsonObject
                   ?? throw new InvalidOperationException("angular.json is not a JSON object.");

        if (root["projects"] is not JsonObject projects)
        {
            return angularJson;
        }

        foreach (var project in projects)
        {
            if (project.Value?["architect"]?["serve"] is not JsonObject serve)
            {
                continue;
            }

            if (serve["options"] is not JsonObject options)
            {
                options = [];
                serve["options"] = options;
            }

            options["proxyConfig"] = "proxy.conf.json";
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>The client's entry stylesheet when the project took Tailwind.</summary>
    /// <remarks>
    ///     One import, because that is all v4 needs. The Vite plugin detects the sources itself from the
    ///     project, so there is no content array to keep in step with where the components live.
    /// </remarks>
    private const string SpaTailwindCss =
        """
        @import "tailwindcss";

        """;

    /// <summary>
    ///     The Tailwind range the front-end templates pin. Deliberately beside the C# side's version: the
    ///     two paths must not drift, or the same app would style differently depending on which half of it
    ///     built the stylesheet.
    /// </summary>
    private const string TailwindRange = "^4.3.0";

    /// <summary>What the patch says it is adding, for the line the command prints.</summary>
    private static string Dependencies(SpaFramework framework) =>
        framework.RouterPackage is null
            ? framework.QueryPackage
            : framework.QueryPackage + " and " + framework.RouterPackage;

    /// <summary>
    ///     Adds this framework's TanStack packages to whatever <c>create-vite</c> wrote, leaving the rest
    ///     of the file alone.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Parsed and re-serialised rather than string-spliced: the scaffolder's <c>package.json</c> is
    ///         not ours and its shape is free to change. Idempotent, so re-running <c>rask new --force</c>
    ///         over an existing client does not produce a duplicate key or a second copy of a range.
    ///     </para>
    ///     <para>
    ///         Svelte's <c>build</c> script is rewritten as well, and that is the one script edit this
    ///         template makes. create-vite gives Svelte a bare <c>vite build</c> — type checking lives in a
    ///         separate <c>check</c> script, because <c>tsc</c> cannot read a <c>.svelte</c> file. Left
    ///         alone, renaming a C# property would break nothing at build time and surface on the wire,
    ///         which is precisely the failure the generated contracts exist to prevent. Every other
    ///         framework's template already runs its type-checker in <c>build</c>.
    ///     </para>
    /// </remarks>
    internal static string AddClientDependencies(
        string packageJson, SpaFramework framework, bool tailwind = false)
    {
        ArgumentNullException.ThrowIfNull(framework);

        var root = JsonNode.Parse(packageJson) as JsonObject
                   ?? throw new InvalidOperationException("package.json is not a JSON object.");

        if (root["dependencies"] is not JsonObject dependencies)
        {
            dependencies = [];
            root["dependencies"] = dependencies;
        }

        dependencies[framework.QueryPackage] = framework.QueryVersion;
        if (framework.RouterPackage is { } router)
        {
            dependencies[router] = framework.RouterVersion;
        }

        if (tailwind)
        {
            // The Vite plugin, not the standalone binary. A front-end project already has node and a
            // bundler; making it shell out to a downloaded binary instead would be Rask insisting on its
            // own mechanism in the one place the ecosystem's is better — and it would give up the
            // bundler's own hot reload for CSS.
            dependencies["tailwindcss"] = TailwindRange;
            dependencies["@tailwindcss/vite"] = TailwindRange;
        }

        if (framework.Key == "svelte" && root["scripts"] is JsonObject scripts)
        {
            scripts["build"] = "svelte-check --tsconfig ./tsconfig.app.json && vite build";
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>Adds the generated-contracts directory to the client's own ignore file.</summary>
    /// <remarks>
    ///     They are build output: the MSBuild task rewrites them from the server assembly on every build, so
    ///     committing them would produce a diff on every contract change and invite a hand-edit that the
    ///     next build silently discards. Appended rather than replaced, and idempotent.
    /// </remarks>
    internal static string IgnoreGeneratedContracts(string gitIgnore)
    {
        const string Entry = "src/rask/";
        if (gitIgnore.Split('\n').Any(line => line.Trim() == Entry))
        {
            return gitIgnore;
        }

        var separator = gitIgnore.EndsWith('\n') ? string.Empty : "\n";
        return gitIgnore + separator
               + "\n# Generated from the server's CQRS contracts on every build (Rask.Spa.Hosting).\n"
               + Entry + "\n";
    }

    private static string SpaServerCsproj(ServerBatteries batteries, SpaFramework framework, string name, string version)
    {
        var refs = new StringBuilder();

        // Skip(3): Rask.Cqrs, Rask.Cqrs.Server and Rask.Spa.Hosting are written below by hand, each with
        // the comment explaining what it brings.
        foreach (var package in SpaServerPackages(batteries).Skip(3))
        {
            refs.Append($"\n    <PackageReference Include=\"{package}\" Version=\"{version}\"/>");
        }

        // Only when it is not the default. Angular nests its output under the project name, and a host
        // pointed at the wrong directory serves the "nothing built yet" page after a build that succeeded.
        var dist = framework.DistFor(name) == "dist"
            ? string.Empty
            : $"\n    <!-- {framework.DisplayName} nests its output under the project name. -->"
              + $"\n    <RaskSpaDistDir>{framework.DistFor(name)}</RaskSpaDistDir>";

        // Named in the "nothing built yet" page the host serves in Development, so it points at the dev
        // server this framework actually runs rather than at Vite's default.
        var devServer = $"\n    <RaskSpaDevServerUrl>{framework.DevServerUrl}</RaskSpaDevServerUrl>";

        var litestream = batteries.Data
            ? "\n    <!-- The litestream binary ships in the Docker image, not fetched at build time. -->"
              + "\n    <RaskLitestreamDownload>false</RaskLitestreamDownload>"
            : "";

        return $"""
        <Project Sdk="Microsoft.NET.Sdk.Web">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>{dist}{devServer}{litestream}
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Rask.Cqrs" Version="{version}"/>
            <PackageReference Include="Rask.Cqrs.Server" Version="{version}"/>
            <!--
              Finds the sibling Company.RaskServer.Client by convention (a .Server project looks for a
              .Client holding a package.json), runs its install and build, writes the generated TypeScript
              contracts into its src/rask/, and copies the bundle into wwwroot on publish. Set
              RaskSpaClientDir to point somewhere else, or -p:RaskSpaBuild=false to skip node entirely —
              the API still works, and the site serves a page saying there is nothing built yet.
            -->
            <PackageReference Include="Rask.Spa.Hosting" Version="{version}"/>{refs}
          </ItemGroup>

        </Project>

        """;
    }

    /// <summary>
    ///     The packages the host needs. The first three are written into the csproj by hand (with their own
    ///     comments), so the caller skips them when appending the rest.
    /// </summary>
    private static List<string> SpaServerPackages(ServerBatteries batteries)
    {
        // Bootstrap is a C#-component library and this host renders no components; the front end owns its
        // own styling. Cqrs is cleared because Rask.Cqrs.Server supersedes it and both are listed by hand.
        var packages = ServerPackages(batteries with { Styling = Styling.Plain, Cqrs = false });
        packages.Remove("Rask.Server");
        packages.Remove("Rask.Cqrs");

        packages.Insert(0, "Rask.Spa.Hosting");
        packages.Insert(0, "Rask.Cqrs.Server");
        packages.Insert(0, "Rask.Cqrs");
        return packages;
    }

    private static string SpaServerProgram(ServerBatteries batteries)
    {
        var sb = new StringBuilder();
        if (batteries.Data)
        {
            // AppDbContext. It lands in the .Server project's own namespace, the way the wasm-hosted
            // template's does, because that is the only project in the solution with a disk to put a
            // database on.
            sb.Append($"using {NameToken}.Server.Features.Shared;\n");
        }

        sb.Append("using Rask.Cqrs.Server;\n");
        sb.Append("using Rask.Spa.Hosting;\n");
        sb.Append(DatabaseAndBatteryUsings(batteries));

        sb.Append("""

            var builder = WebApplication.CreateBuilder(args);

            // AddRaskCqrsServer registers the mediator AND the endpoint pair the front end dispatches
            // through. The TypeScript the client imports is generated from these same message records at
            // build time, so the two halves cannot disagree about a payload or a result.
            //
            // RequireAuthenticatedUser is OFF because this template has no authentication to require —
            // left on, every message would answer 401 and nothing would work. Add a cookie or JWT scheme
            // and DELETE this argument: the default is on for a reason, and a message reachable by anyone
            // is a decision worth making per app.
            builder.Services.AddRaskCqrsServer(o => o.RequireAuthenticatedUser = false);

            builder.Services.AddSingleton<Company.RaskServer.Server.Features.Hello.VisitCounter>();

            // A liveness/readiness endpoint (mapped below). `rask deploy` probes it to gate the blue-green
            // swap; also useful for any load balancer or orchestrator.
            builder.Services.AddHealthChecks();

            """.TrimStart('\n'));

        sb.Append(ShutdownBudgetBlock(batteries.Data));
        AppendDatabaseAndBatteries(sb, batteries);

        sb.Append("""

            var app = builder.Build();

            """.TrimStart('\n'));

        Block(sb, """
            // The endpoint pair every dispatched message arrives on: GET for queries, POST for commands,
            // both under /_rask/cqrs/request/{name}. Two routes however many messages the app grows, and
            // the verb carries what IQuery and ICommand already declare — so a command is 405 on GET and
            // cannot be triggered by a URL, a prefetch or a link scanner.
            //
            // Mapped BEFORE UseRaskSpa. That call ends the pipeline with a fallback to index.html, and an
            // endpoint added after it would be shadowed by the fallback rather than reached.
            app.MapRaskCqrs();

            app.MapHealthChecks("/healthz");
            """);

        Block(sb, """
            // Serves the bundler's dist/ — correct MIME types, bundler-aware cache headers, precompressed
            // siblings, and a SPA fallback that still 404s a missing asset rather than answering it with
            // HTML. In development, before anything is built, it explains where the dev server is instead.
            app.UseRaskSpa();

            app.Run();
            """);

        return sb.ToString();
    }

    private const string SpaMessages =
        """
        using Rask.Cqrs;

        namespace Company.RaskServer.Server.Features.Hello;

        /// <summary>The greeting the front end asks for on load.</summary>
        /// <remarks>
        ///     Every public property becomes a TypeScript type at build time. SeenAt is a DateTimeOffset
        ///     rather than a DateTime on purpose: it carries its offset onto the wire, so the browser reads
        ///     an unambiguous instant. A bare DateTime would arrive as a local time on whichever machine
        ///     parsed it.
        /// </remarks>
        public sealed record Greeting(string Message, DateTimeOffset SeenAt, int Visits);

        public sealed record GetGreeting(string Name) : IQuery<Greeting>;

        public sealed record RecordVisit(string Name) : ICommand<int>;

        """;

    private const string SpaHandlers =
        """
        using Rask.Cqrs;

        namespace Company.RaskServer.Server.Features.Hello;

        // In-memory, because a starter should run before it has a database. Swap it for a real store —
        // `rask new --template react --data` scaffolds one.
        public sealed class VisitCounter
        {
            private int _visits;

            public int Visits => Volatile.Read(ref _visits);

            public int Record() => Interlocked.Increment(ref _visits);
        }

        public sealed class GetGreetingHandler(VisitCounter counter) : IQueryHandler<GetGreeting, Greeting>
        {
            public Task<Greeting> HandleAsync(GetGreeting query, CancellationToken cancellationToken) =>
                Task.FromResult(new Greeting($"Hello, {query.Name}!", DateTimeOffset.UtcNow, counter.Visits));
        }

        public sealed class RecordVisitHandler(VisitCounter counter) : ICommandHandler<RecordVisit, int>
        {
            public Task<int> HandleAsync(RecordVisit command, CancellationToken cancellationToken) =>
                Task.FromResult(counter.Record());
        }

        """;

    private static string SpaViteConfig(SpaFramework framework, bool tailwind)
    {
        // Lit needs no plugin: its components are standard custom elements and its decorators are
        // TypeScript's, so create-vite ships that template with no vite.config.ts at all. This is the
        // file that creates one, purely to carry the dev proxy.
        var import_ = framework.PluginImport.Length == 0
            ? string.Empty
            : "import " + framework.PluginImport + "\n";

        // Tailwind's own Vite plugin rather than the standalone binary the C# hosts use: this project
        // already has node and a bundler, and the plugin gives the bundler's hot reload for CSS too.
        var tailwindImport = tailwind ? "import tailwindcss from '@tailwindcss/vite'\n" : string.Empty;
        var tailwindPlugin = tailwind
            ? framework.PluginCall.Length == 0 ? "tailwindcss()" : ", tailwindcss()"
            : string.Empty;

        return $$"""
        {{import_}}{{tailwindImport}}import { defineConfig } from 'vite'

        // https://vite.dev/config/
        export default defineConfig({
          plugins: [{{framework.PluginCall}}{{tailwindPlugin}}],
          server: {
            // In development the browser talks to Vite, and Vite forwards the CQRS calls to the ASP.NET
            // host — so HMR is native and instant, and there is no CORS to configure because the browser
            // only ever sees one origin. In production this proxy is not used at all: the host serves the
            // built bundle and answers /_rask itself.
            proxy: {
              '/_rask': {
                target: 'http://localhost:5000',
                changeOrigin: true,
              },
            },
          },
        })

        """;
    }

    private const string SpaDockerfile =
        """
        # Two toolchains, two stages: node builds the front end, the .NET SDK builds the host. The final
        # image carries neither.
        FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
        WORKDIR /src

        # Node, for the client build the Rask.Spa.Hosting targets run during `dotnet publish`.
        RUN apt-get update \
         && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
         && curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
         && apt-get install -y --no-install-recommends nodejs \
         && rm -rf /var/lib/apt/lists/*

        # Restore against the manifests alone, so a source-only change does not invalidate this layer.
        COPY ["Company.RaskServer.Server/Company.RaskServer.Server.csproj", "Company.RaskServer.Server/"]
        RUN dotnet restore "Company.RaskServer.Server/Company.RaskServer.Server.csproj"

        COPY ["Company.RaskServer.Client/package.json", "Company.RaskServer.Client/package-lock.json*", "Company.RaskServer.Client/"]
        RUN cd Company.RaskServer.Client && npm ci --no-audit --no-fund || npm install --no-audit --no-fund

        COPY . .
        RUN dotnet publish "Company.RaskServer.Server/Company.RaskServer.Server.csproj" -c Release -o /app/publish

        FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
        WORKDIR /app
        COPY --from=build /app/publish .

        # 8080, matching the other templates so `rask deploy` maps the same port.
        ENV ASPNETCORE_URLS=http://+:8080
        EXPOSE 8080
        ENTRYPOINT ["dotnet", "Company.RaskServer.Server.dll"]

        """;

    private const string SpaLaunchSettings =
        """
        {
          "profiles": {
            "Company.RaskServer.Server": {
              "commandName": "Project",
              "launchBrowser": false,
              "applicationUrl": "http://localhost:5000",
              "environmentVariables": {
                "ASPNETCORE_ENVIRONMENT": "Development"
              }
            }
          }
        }

        """;

    private static string SpaReadme(SpaFramework framework) =>
        $$"""
        # Company.RaskServer

        A {{framework.DisplayName}} front end on an ASP.NET host, talking to it over Rask's CQRS wire.

        ## Layout

        | | |
        |---|---|
        | `Company.RaskServer.Server/` | The ASP.NET host: the message records, their handlers, and the JSON endpoint the client dispatches through. |
        | `Company.RaskServer.Client/` | The {{framework.DisplayName}} app, as `create-vite` scaffolds it, plus four files Rask overlays. |
        | `Company.RaskServer.Client/src/rask/` | Generated on every build from the server's contracts. Gitignored — do not edit. |

        ## Running it

        ```bash
        rask dev
        ```

        The browser talks to Vite, which forwards `/_rask` to the ASP.NET host. In production the host
        serves the built bundle itself and answers `/_rask` directly, so there is no proxy in the way.

        ## Adding a message

        Add a record to `Features/Hello/Messages.cs` and a handler beside it. The next build writes its
        TypeScript into `src/rask/`, and the front end imports a factory with the payload and result types
        already attached:

        ```ts
        const order = await rask.dispatch(getOrder({ id }))
        ```

        A `DateTimeOffset` arrives as a real `Date`. A `DateOnly` deliberately does not — it stays a
        `YYYY-MM-DD` string, because `new Date("2026-08-25")` is UTC midnight and would render as the
        previous day for anyone west of UTC.

        ## The client stays TypeScript

        The contracts are generated as `.ts`, and the host checks that the client can compile them: a
        client with no `tsconfig.json` fails the build with `RASKSPA004`. That is the whole guarantee
        — a renamed C# property becomes a front-end compile error rather than a wrong payload — so
        dropping to JavaScript would keep the imports working and quietly remove every check behind
        them.

        """;

    private static string SpaNextSteps(string name, SpaFramework framework, bool docker)
    {
        var steps = new StringBuilder();
        steps.AppendLine($"Next steps for {name} ({framework.DisplayName}):");
        steps.AppendLine();
        steps.AppendLine($"  cd {name}");
        steps.AppendLine("  rask dev            # the host, and the client's dev server, together");
        steps.AppendLine();
        steps.AppendLine("The first build installs the client's dependencies and writes its generated");
        steps.AppendLine($"contracts into {name}.Client/src/rask/ — that directory is gitignored, because it is");
        steps.AppendLine("rewritten from the server's message records every time they change.");

        if (docker)
        {
            steps.AppendLine();
            steps.AppendLine("  docker build -t " + name.ToLowerInvariant() + " .   # node builds the client, the SDK builds the host");
        }

        return steps.ToString();
    }
}

/// <summary>
///     One front-end framework, in the terms the scaffolding needs: what to ask <c>create-vite</c> for,
///     and what its Vite plugin is called.
/// </summary>
/// <remarks>
///     <see cref="ViteTemplate" /> names a <b>TypeScript</b> variant, always. Rask supports TypeScript
///     single-page app clients, and the JavaScript half of every create-vite pair would scaffold a client
///     the host then refuses to build (RASKSPA004) — the generated contracts are <c>.ts</c>, and a client
///     that cannot check them gets none of what the template exists to give.
/// </remarks>
internal sealed record SpaFramework(
    string Key,
    string DisplayName,
    string ViteTemplate,
    string PluginImport,
    string PluginCall,
    string QueryPackage,
    string QueryVersion,
    string? RouterPackage,
    string? RouterVersion,
    IReadOnlyList<(string Path, string Content)> ClientFiles)
{
    /// <summary>
    ///     Whether Rask writes the client's <c>vite.config.ts</c>.
    /// </summary>
    /// <remarks>
    ///     False for Angular. Angular's build <em>is</em> Vite-based — <c>@angular/build:application</c>
    ///     has run its dev server on Vite since v17 — but the config is Angular's, not yours: the proxy is
    ///     declared in <c>proxy.conf.json</c> and pointed at from <c>angular.json</c>, and writing a
    ///     <c>vite.config.ts</c> beside that would be a file nothing reads.
    /// </remarks>
    public bool WritesViteConfig { get; init; } = true;

    /// <summary>Where the bundler writes, relative to the client.</summary>
    public string DistDir { get; init; } = "dist";

    /// <summary>Where the framework's own dev server listens, for the browser and the banner.</summary>
    public string DevServerUrl { get; init; } = "http://localhost:5173";

    /// <summary>
    ///     The global stylesheet this framework's scaffolder writes, and which its entry point imports.
    /// </summary>
    /// <remarks>
    ///     Not the same file in any two of them — index.css for React, Preact, Solid and Lit, style.css
    ///     for Vue, app.css for Svelte, styles.css for Angular. Overlaying the wrong name does not fail:
    ///     it lands beside the real one, nothing imports it, and the app builds with no Tailwind in it at
    ///     all.
    /// </remarks>
    public string GlobalStylesheet { get; init; } = "src/index.css";

    /// <summary>
    ///     The command that scaffolds the client, given the solution name.
    /// </summary>
    /// <remarks>
    ///     Per framework rather than one <c>create-vite</c> call, because Angular's own scaffolder is
    ///     <c>ng new</c> and that is the one it should get: the whole argument of these templates is that
    ///     the framework's own conventions win.
    /// </remarks>
    public Func<string, IReadOnlyList<string>> Scaffolder { get; init; } =
        static _ => throw new InvalidOperationException("No scaffolder was configured.");

    /// <summary>The tool the scaffolder runs, named in the "install this" message when it is missing.</summary>
    public string ScaffolderName { get; init; } = "create-vite";

    /// <summary>
    ///     The client's name in its own ecosystem's terms: lower-case, dashes, no dots.
    /// </summary>
    /// <remarks>
    ///     Angular validates its project name and rejects <c>Shop.Client</c> outright, so the CLI is given
    ///     <c>shop-client</c> with <c>--directory Shop.Client</c> — which is also what decides where the
    ///     bundle lands, since Angular's default output is <c>dist/&lt;project&gt;/browser</c>.
    /// </remarks>
    internal static string ClientPackageName(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray();
        return new string(chars).Trim('-') + "-client";
    }

    /// <summary>Where this framework's bundle lands for a solution called <paramref name="name" />.</summary>
    public string DistFor(string name) =>
        DistDir.Replace("{client}", ClientPackageName(name), StringComparison.Ordinal);

    /// <summary>
    ///     Ranges rather than exact versions: the client's own lockfile is what makes a build
    ///     reproducible, and pinning exactly here would freeze every scaffolded app on whatever was
    ///     current the day its Rask shipped.
    /// </summary>
    private const string QueryRange = "^5.102.0";

    /// <summary>Svelte Query versions independently of the others, and is already at 6.</summary>
    private const string SvelteQueryRange = "^6.1.0";

    /// <summary>Lit Query is young, and its 0.x means a minor can break — so the range is tighter.</summary>
    private const string LitQueryRange = "^0.2.0";

    private const string RouterRange = "^1.170.0";

    /// <summary>The default: ask create-vite for a framework's TypeScript template.</summary>
    private static Func<string, IReadOnlyList<string>> Vite(string template) =>
        name => ["--yes", "create-vite@latest", name + ".Client", "--template", template];

    public static readonly SpaFramework React = new(
        "react", "React", "react-ts",
        "react from '@vitejs/plugin-react'", "react()",
        "@tanstack/react-query", QueryRange,
        "@tanstack/react-router", RouterRange,
        SpaClientSources.React)
    {
        Scaffolder = Vite("react-ts"),
    };

    /// <summary>
    ///     Preact, on the React adapter.
    /// </summary>
    /// <remarks>
    ///     There is no <c>@tanstack/preact-query</c> and there does not need to be: create-vite's Preact
    ///     template already maps <c>react</c> and <c>react-dom</c> to <c>preact/compat</c> in its
    ///     tsconfig, and <c>@preact/preset-vite</c> does the same at build time — so the React adapter
    ///     type-checks and bundles here unchanged.
    /// </remarks>
    public static readonly SpaFramework Preact = new(
        "preact", "Preact", "preact-ts",
        "preact from '@preact/preset-vite'", "preact()",
        "@tanstack/react-query", QueryRange,
        null, null,
        SpaClientSources.Preact)
    {
        Scaffolder = Vite("preact-ts"),
    };

    public static readonly SpaFramework Solid = new(
        "solid", "Solid", "solid-ts",
        "solid from 'vite-plugin-solid'", "solid()",
        "@tanstack/solid-query", QueryRange,
        "@tanstack/solid-router", RouterRange,
        SpaClientSources.Solid)
    {
        Scaffolder = Vite("solid-ts"),
    };

    public static readonly SpaFramework Vue = new(
        "vue", "Vue", "vue-ts",
        "vue from '@vitejs/plugin-vue'", "vue()",
        "@tanstack/vue-query", QueryRange,
        null, null,
        SpaClientSources.Vue)
    {
        GlobalStylesheet = "src/style.css",
        Scaffolder = Vite("vue-ts"),
    };

    /// <summary>
    ///     Svelte. No TanStack Router — it ships React and Solid adapters only, and SvelteKit is what
    ///     this ecosystem reaches for instead.
    /// </summary>
    public static readonly SpaFramework Svelte = new(
        "svelte", "Svelte", "svelte-ts",
        "{ svelte } from '@sveltejs/vite-plugin-svelte'", "svelte()",
        "@tanstack/svelte-query", SvelteQueryRange,
        null, null,
        SpaClientSources.Svelte)
    {
        GlobalStylesheet = "src/app.css",
        Scaffolder = Vite("svelte-ts"),
    };

    /// <summary>
    ///     Lit, which needs no Vite plugin at all — its components are standard custom elements, and
    ///     its decorators are TypeScript's.
    /// </summary>
    public static readonly SpaFramework Lit = new(
        "lit", "Lit", "lit-ts",
        string.Empty, string.Empty,
        "@tanstack/lit-query", LitQueryRange,
        null, null,
        SpaClientSources.Lit)
    {
        Scaffolder = Vite("lit-ts"),
    };

    /// <summary>
    ///     Angular, scaffolded by its own CLI.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The one framework here <c>create-vite</c> does not ship a template for. Angular's build is
    ///         Vite-based — <c>@angular/build:application</c> has run its dev server on Vite since v17 —
    ///         but the config belongs to Angular, so there is no <c>vite.config.ts</c> to write: the proxy
    ///         is declared in <c>proxy.conf.json</c> and pointed at from <c>angular.json</c>, and the
    ///         bundle lands in <c>dist/&lt;project&gt;/browser</c> rather than <c>dist</c>.
    ///     </para>
    ///     <para>
    ///         <c>--skip-install</c>: the Rask.Spa.Hosting targets run the install on the first build, the
    ///         way they do for every other client. <c>--skip-git</c> because <c>rask new</c> initialises one
    ///         repository at the solution root, and a second nested inside it is not what anyone wants.
    ///     </para>
    ///     <para>
    ///         Angular's CLI has its own Node floor, higher than Vite's. When it refuses to run it says so
    ///         itself and names the version it wants, which is a better message than one written here.
    ///     </para>
    /// </remarks>
    public static readonly SpaFramework Angular = new(
        "angular", "Angular", string.Empty,
        string.Empty, string.Empty,
        "@tanstack/angular-query-experimental", QueryRange,
        null, null,
        SpaClientSources.Angular)
    {
        GlobalStylesheet = "src/styles.css",
        WritesViteConfig = false,
        DistDir = "dist/{client}/browser",
        DevServerUrl = "http://localhost:4200",
        ScaffolderName = "the Angular CLI",
        Scaffolder = name =>
        [
            "--yes", "@angular/cli@latest", "new", ClientPackageName(name),
            "--directory", name + ".Client",
            "--style", "css", "--ssr", "false",
            "--skip-git", "--skip-install", "--defaults",
        ],
    };

    /// <summary>Every framework <c>rask new</c> can scaffold a client for.</summary>
    public static IReadOnlyList<SpaFramework> All { get; } = [React, Preact, Vue, Angular, Solid, Svelte, Lit];

    public static bool TryGet(string key, out SpaFramework framework)
    {
        foreach (var candidate in All)
        {
            if (candidate.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                framework = candidate;
                return true;
            }
        }

        framework = React;
        return false;
    }
}
