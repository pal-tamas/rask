using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rask.Cli.Scaffolding;

// The meta framework templates: an ASP.NET host that answers CQRS over JSON and supervises the
// framework's own Node server, beside a front end that framework's OWN creator produces. Rask overlays
// one config file onto it and patches two — everything else is whatever `nuxi` or `create-next-app`
// ships today, which is the point.
//
// The difference from the SPA lane is the whole reason this is a separate generator: there, Rask serves
// a static bundle and node is gone after the build. Here the framework keeps its own server, so what
// has to be arranged is a SECOND process rather than a directory of files — its adapter must emit a
// node server, and its dev server must proxy /_rask back to the host.
internal static partial class ProjectGenerator
{
    /// <summary>
    ///     Generates a meta framework app: <c>{name}</c> (ASP.NET + CQRS, supervising node) with a
    ///     <c>Client</c> folder inside it, scaffolded by the framework's own tool and then overlaid.
    /// </summary>
    /// <remarks>
    ///     One project, and a <c>Client</c> FOLDER rather than a sibling project — the same shape the SPA
    ///     lane settled on in #970, and for a stronger reason here: a meta framework app has no separate
    ///     client artifact for a host to reference at all. It has a server of its own.
    /// </remarks>
    public static ScaffoldResult GenerateMeta(
        string targetDirectory,
        string name,
        MetaTemplate framework,
        ServerBatteries requested,
        string version)
    {
        var batteries = requested.Normalized() with { Cqrs = true };

        var files = new List<(string Path, string Content)>
        {
            ($"{NameToken}/{NameToken}.csproj", MetaCsproj(batteries, framework, version)),
            ($"{NameToken}/Program.cs", MetaProgram(batteries)),
            ($"{NameToken}/Features/Hello/Messages.cs", SpaMessages),
            ($"{NameToken}/Features/Hello/HelloHandlers.cs", SpaHandlers),
            ($"{NameToken}/Properties/launchSettings.json", SpaLaunchSettings),
            ($"{NameToken}/appsettings.json", AppSettings),
            ($"{NameToken}/appsettings.Production.json", AppSettingsProduction),

            ("README.md", MetaReadme(framework)),

        };

        // What Rask overlays onto the front end, and the only thing it overlays: the config carrying the
        // two facts the creator cannot know. One is the adapter or preset that emits a NODE SERVER — this
        // lane runs `node <entry>`, so a static or edge preset produces an app the host cannot start at
        // all. The other is the dev proxy that sends /_rask back to the host while the browser is talking
        // to the dev server.
        //
        // A list because SvelteKit needs two: the adapter is declared in svelte.config.js and the proxy
        // in vite.config.ts, and writing only the first leaves a dev session whose every dispatch 404s.
        foreach (var (path, content) in framework.ConfigFiles)
        {
            files.Add(($"{NameToken}/{framework.AppDir}/{path}", content));
        }

        // Tailwind, for the two frameworks whose creators cannot be asked for it. The other four take
        // it from their own creator — which is the better answer here, since a scaffold that produced
        // something the framework's own documentation does not describe would be worth less than none.
        if (framework.TailwindStylesheet is { Length: > 0 } stylesheet)
        {
            files.Add(($"{NameToken}/{framework.AppDir}/{stylesheet}", SpaTailwindCss));

            if (framework.TailwindThroughPostcss)
            {
                // Vite reads a PostCSS config on its own, so this needs no edit to a plugins array that
                // belongs to the framework — which for Analog is the same file the Rask dev proxy is
                // patched into, and the one carrying its Angular plugin.
                files.Add(($"{NameToken}/{framework.AppDir}/.postcssrc.json", SpaTailwindPostcssRc));
            }
        }

        if (batteries.Data)
        {
            files.Add(($"{NameToken}/Features/Shared/AppDbContext.cs", AppDbContextCs(batteries)));
        }

        if (batteries.Docker)
        {
            files.Add(("Dockerfile", MetaDockerfile(framework)));
            files.Add((".dockerignore", DockerIgnore));
        }

        files.AddRange(ProjectHygiene($"{NameToken}/{NameToken}.csproj"));

        var scaffoldFiles = Materialize(targetDirectory, name, files);
        var client = System.IO.Path.Combine(targetDirectory, name, framework.AppDir);

        return new ScaffoldResult(scaffoldFiles, MetaNextSteps(name, framework, batteries.Docker))
        {
            Packages = ["Rask.Cqrs", "Rask.Cqrs.Server", "Rask.Meta.Hosting"],
            RestoreTarget = $"{name}.slnx",
            ExternalScaffolds =
            [
                new ExternalScaffold(
                    "npx",
                    framework.Scaffolder(name),
                    $"Scaffolding the {framework.DisplayName} app with {framework.ScaffolderName}…",
                    // The Node LTS line, not the build floor: these creators track Active LTS and raise
                    // their own floors on their own schedule, and naming the build's 22.12 here sends
                    // people to install a Node that then fails the scaffold at exit 1 — after the project
                    // directory already exists (#886).
                    NodeRequirement.ScaffoldHint(framework.ScaffolderName))
                {
                    // Every creator on this lane is run from INSIDE the project directory with a target
                    // of `client`, rather than being handed `Shop/client`. Three of the six refuse a
                    // nested path or a capital letter in it — two by exiting, and create-analog by
                    // stopping to ask, which inside `rask new` is a hang. One rule for all six is worth
                    // more than three special cases and a fourth waiting to be discovered.
                    WorkingSubdirectory = name,
                },
            ],
            Patches = MetaPatches(client, framework),
        };
    }

    /// <summary>The edits made to what the front end's own creator wrote.</summary>
    private static IReadOnlyList<ScaffoldPatch> MetaPatches(string client, MetaTemplate framework)
    {
        var patches = new List<ScaffoldPatch>
        {
            new(
                System.IO.Path.Combine(client, ".gitignore"),
                gitIgnore => IgnoreGeneratedDirectory(
                    gitIgnore, framework.GeneratedDir + "/", "Rask.Meta.Hosting"),
                "ignoring the generated contracts"),
        };

        if (framework.TailwindStylesheet is { Length: > 0 })
        {
            patches.Add(new ScaffoldPatch(
                System.IO.Path.Combine(client, "package.json"),
                json => AddMetaTailwind(json, framework),
                "adding Tailwind"));
        }

        // The frameworks whose own Vite config carries the work that makes this lane possible — the node
        // adapter, the Start plugin, Nitro. Patched, never written over.
        if (framework.ViteConfigFile is { Length: > 0 } vite)
        {
            patches.Add(new ScaffoldPatch(
                System.IO.Path.Combine(client, vite),
                config => AddRaskDevProxy(config),
                "proxying /_rask to the host in development"));
        }

        // `import { rask } from '@rask/client'` rather than a relative path out of a route file, and the
        // same specifier whichever framework this is. The build writes tsconfig.rask.json beside the
        // generated code; this is the one line that turns it on, and it goes in the tsconfig the creator
        // wrote because that is the one the framework actually reads.
        if (framework.TsConfigFile is { Length: > 0 } tsConfig)
        {
            patches.Add(new ScaffoldPatch(
                System.IO.Path.Combine(client, tsConfig),
                json => ExtendRaskTsConfig(json, framework.GeneratedDir),
                "pointing tsconfig at the @rask/* aliases"));
        }

        return patches;
    }

    /// <summary>
    ///     Points the front end's tsconfig at the <c>tsconfig.rask.json</c> the build writes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         APPENDED to whatever the creator already extends, never assigned over it. Three of these
    ///         frameworks extend a generated config that carries their whole type environment — SvelteKit's
    ///         <c>./.svelte-kit/tsconfig.json</c>, Analog's Angular base — and replacing that line does not
    ///         fail the build: it silently removes the framework's own types, and the first error the
    ///         developer sees is about their own code.
    ///     </para>
    ///     <para>
    ///         An array is legal from TypeScript 5.0 and resolves left to right, so Rask's aliases are
    ///         added last and win no argument they should not.
    ///     </para>
    ///     <para>
    ///         Idempotent, and a no-op on a file this does not recognise: failing a scaffold over an
    ///         import alias would be worse than saying it did not happen.
    ///     </para>
    /// </remarks>
    internal static string ExtendRaskTsConfig(string tsConfig, string generatedDir)
    {
        var target = $"./{generatedDir}/tsconfig.rask.json";
        if (tsConfig.Contains(target, StringComparison.Ordinal))
        {
            return tsConfig;
        }

        // Edited as TEXT rather than parsed and re-serialised, because a tsconfig is JSONC: Angular's —
        // and so Analog's — ships full of explanatory comments and links, and a JSON round-trip would
        // silently delete every one of them while reformatting a file the developer owns.
        var single = Regex.Match(tsConfig, @"""extends""\s*:\s*""(?<base>[^""]*)""");
        if (single.Success)
        {
            return tsConfig
                .Remove(single.Index, single.Length)
                .Insert(single.Index, $"\"extends\": [\"{single.Groups["base"].Value}\", \"{target}\"]");
        }

        var array = Regex.Match(tsConfig, @"""extends""\s*:\s*\[(?<items>[^\]]*)\]");
        if (array.Success)
        {
            var items = array.Groups["items"].Value.TrimEnd();
            var separator = items.Length == 0 ? string.Empty : ", ";
            return tsConfig
                .Remove(array.Index, array.Length)
                .Insert(array.Index, $"\"extends\": [{items}{separator}\"{target}\"]");
        }

        // No extends at all: added as the first member, which is where a hand-written tsconfig puts it
        // and where a reader looks for it.
        var brace = tsConfig.IndexOf('{', StringComparison.Ordinal);
        return brace < 0 ? tsConfig : tsConfig.Insert(brace + 1, $"\n  \"extends\": \"{target}\",");
    }

    /// <summary>Adds Tailwind to a front end whose own creator would not.</summary>
    /// <remarks>
    ///     The Vite plugin or the PostCSS adapter, never both: they are two adapters for one compiler,
    ///     and installing the one nothing reads is silent — the build succeeds with no utilities in the
    ///     output, which looks like a Tailwind problem and is not.
    /// </remarks>
    internal static string AddMetaTailwind(string packageJson, MetaTemplate framework)
    {
        if (JsonNode.Parse(packageJson) is not JsonObject root)
        {
            return packageJson;
        }

        if (root["dependencies"] is not JsonObject dependencies)
        {
            dependencies = [];
            root["dependencies"] = dependencies;
        }

        dependencies["tailwindcss"] = TailwindRange;
        dependencies[framework.TailwindThroughPostcss ? "@tailwindcss/postcss" : "@tailwindcss/vite"] =
            TailwindRange;

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>
    ///     Adds the development proxy to a front end's own Vite config.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         In a <c>rask dev</c> session the browser talks to the framework's dev server, not to
    ///         Kestrel — that is what makes hot module replacement native and full-speed. Without this,
    ///         every dispatch from the page 404s against the framework's own router, because
    ///         <c>/_rask</c> means nothing to it.
    ///     </para>
    ///     <para>
    ///         Inserted into the file the creator wrote rather than replacing it. SvelteKit's holds the
    ///         node adapter and the runes option; TanStack's holds the Start plugin and Nitro. Both are
    ///         exactly what makes the build produce a server this host can run.
    ///     </para>
    ///     <para>
    ///         A no-op when the config already declares a <c>server</c> block: appending a second one is
    ///         not a merge but a duplicate key, which TypeScript rejects outright. Reporting that the
    ///         proxy was not added is better than handing back a front end that will not compile.
    ///     </para>
    /// </remarks>
    internal static string AddRaskDevProxy(string viteConfig)
    {
        if (viteConfig.Contains("/_rask", StringComparison.Ordinal)
            || Regex.IsMatch(viteConfig, @"(^|[\s{,])server\s*:"))
        {
            return viteConfig;
        }

        // Two shapes, and telling them apart matters more than it looks. Analog's config is
        // `defineConfig(({ mode }) => ({ … }))` — a function of the Vite mode — so the first brace after
        // defineConfig( is the DESTRUCTURING one, and inserting there writes the proxy into the
        // parameter list. The file still parses as far as the eye goes and then fails to compile.
        var arrow = Regex.Match(viteConfig, @"defineConfig\s*\(\s*(?:async\s*)?\(?[^)]*\)?\s*=>\s*\(\s*\{");
        var literal = Regex.Match(viteConfig, @"defineConfig\s*\(\s*\{");

        var opening = arrow.Success ? arrow : literal;
        if (!opening.Success)
        {
            return viteConfig;
        }

        var brace = opening.Index + opening.Length - 1;

        const string Proxy = """

          server: {
            // In development the browser talks to this dev server, and it forwards the CQRS calls to
            // the ASP.NET host — so HMR is native, and there is no CORS to configure because the browser
            // only ever sees one origin. In production this is not used at all: Kestrel owns the port
            // and answers /_rask itself.
            proxy: {
              '/_rask': { target: 'http://localhost:5000', changeOrigin: true }
            }
          },
        """;

        return viteConfig.Insert(brace + 1, Proxy);
    }

    /// <summary>
    ///     The container this lane ships: one image, one port — and, unlike every other Rask template, a
    ///     Node runtime inside it.
    /// </summary>
    /// <remarks>
    ///     That is the honest cost of asking a meta framework to render your pages, and it is why the
    ///     final stage here installs node while the SPA lane's discards it after the build. The
    ///     alternative is a second container and a second port, which is the thing this lane exists to
    ///     avoid.
    /// </remarks>
    private static string MetaDockerfile(MetaTemplate framework) =>
        $"""
        # Two toolchains, two stages: node builds the front end, the .NET SDK builds the host. Unlike the
        # TypeScript-SPA template, the FINAL image keeps node — a {framework.DisplayName} app has a server
        # of its own, and this host supervises it as a child process for the life of the container.
        FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
        WORKDIR /src

        RUN apt-get update \
         && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
         && curl -fsSL https://deb.nodesource.com/setup_24.x | bash - \
         && apt-get install -y --no-install-recommends nodejs \
         && rm -rf /var/lib/apt/lists/*

        # Restore against the manifests alone, so a source-only change does not invalidate this layer.
        COPY ["Company.RaskServer/Company.RaskServer.csproj", "Company.RaskServer/"]
        RUN dotnet restore "Company.RaskServer/Company.RaskServer.csproj"

        COPY ["Company.RaskServer/{framework.AppDir}/package.json", "Company.RaskServer/{framework.AppDir}/package-lock.json*", "Company.RaskServer/{framework.AppDir}/"]
        RUN cd Company.RaskServer/{framework.AppDir} && npm ci --no-audit --no-fund || npm install --no-audit --no-fund

        COPY . .
        RUN dotnet publish "Company.RaskServer/Company.RaskServer.csproj" -c Release -o /app/publish

        FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
        WORKDIR /app

        # The one thing this image has that the others do not. The front end's server runs here, bound to
        # loopback and supervised by the host — publishing this container's port cannot expose it.
        RUN apt-get update \
         && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
         && curl -fsSL https://deb.nodesource.com/setup_24.x | bash - \
         && apt-get install -y --no-install-recommends nodejs \
         && rm -rf /var/lib/apt/lists/*

        COPY --from=build /app/publish .

        # 8080, matching the other templates so `rask deploy` maps the same port. The framework's own
        # server is NOT exposed: it listens on 127.0.0.1 and only Kestrel reaches it.
        ENV ASPNETCORE_URLS=http://+:8080
        EXPOSE 8080
        ENTRYPOINT ["dotnet", "Company.RaskServer.dll"]

        """;

    private static string MetaReadme(MetaTemplate framework) =>
        $$"""
        # Company.RaskServer

        A {{framework.DisplayName}} front end on an ASP.NET host: one project, one container, one port.

        ## Layout

        | | |
        |---|---|
        | `Company.RaskServer/` | The ASP.NET host: the message records, their handlers, and the JSON endpoint the front end dispatches through. |
        | `Company.RaskServer/{{framework.AppDir}}/` | The {{framework.DisplayName}} app, as `{{framework.ScaffolderName}}` scaffolds it, plus the config Rask adjusts. |
        | `Company.RaskServer/{{framework.AppDir}}/{{framework.GeneratedDir}}/` | Generated on every build: your C# contracts as TypeScript, and Rask's browser layer. Gitignored — do not edit. |

        ## Running it

        ```bash
        rask dev
        ```

        Two processes. The browser talks to {{framework.DisplayName}}'s own dev server on
        {{framework.DevServerUrl}}, which proxies `/_rask` back to the host — so hot module replacement is
        native and full-speed, with Rask nowhere in its path.

        ## Calling your C#

        ```ts
        import { rask } from '@rask/client'
        import { getGreeting } from '@rask/messages'

        const greeting = await rask.dispatch(getGreeting({ name: 'world' }))
        ```

        `greeting` is typed from the C# record. Rename a property there and this stops compiling, which
        is the entire point — there is no schema file to keep in sync.

        Rask's typed browser APIs are the same import:

        ```ts
        import { getCurrentPosition } from '@rask/browser/geolocation'
        ```

        ## In production

        Kestrel owns the public port. It serves the framework's content-hashed assets itself, forwards
        everything else to the framework's server on loopback, and supervises that process — so ASP.NET
        authentication, rate limiting and health checks sit in front of every request.

        Map your API **before** `app.UseRaskMeta()`: it registers a fallback, and the symptom of getting
        that backwards is an API call answered with a rendered page.

        """;

    private static string MetaNextSteps(string name, MetaTemplate framework, bool docker)
    {
        var steps = new StringBuilder();
        steps.AppendLine($"Next steps for {name} ({framework.DisplayName}):");
        steps.AppendLine();
        steps.AppendLine($"  cd {name}");
        steps.AppendLine("  rask dev            # the host, and the framework's own dev server, together");
        steps.AppendLine();
        steps.AppendLine($"The browser talks to {framework.DisplayName} on {framework.DevServerUrl}, which");
        steps.AppendLine("proxies /_rask back to the host — so hot module replacement is native.");
        steps.AppendLine();
        steps.AppendLine($"The first build installs the front end's dependencies and writes your C# contracts");
        steps.AppendLine($"and Rask's browser layer into {name}/{framework.AppDir}/{framework.GeneratedDir}/ — gitignored,");
        steps.AppendLine("because it is rewritten from the message records every time they change.");

        if (docker)
        {
            steps.AppendLine();
            steps.AppendLine($"  docker build -t {name.ToLowerInvariant()} .");
            steps.AppendLine("The image carries a node runtime, which this lane needs and the TypeScript-SPA");
            steps.AppendLine("template does not: the front end has a server of its own, supervised on loopback.");
        }

        return steps.ToString();
    }

    private static string MetaCsproj(ServerBatteries batteries, MetaTemplate framework, string version)
    {
        var refs = new StringBuilder();

        // Skip(3): Rask.Cqrs, Rask.Cqrs.Server and Rask.Meta.Hosting are written below by hand, each
        // with the comment explaining what it brings.
        foreach (var package in MetaServerPackages(batteries).Skip(3))
        {
            refs.Append($"\n    <PackageReference Include=\"{package}\" Version=\"{version}\"/>");
        }

        // Only when it is not the default. Analog's front end lives in a lowercase folder, because its
        // creator will not scaffold into a directory whose name is not a valid npm package name — and a
        // build that looks in Client/ while the app is in client/ finds nothing on any case-sensitive
        // filesystem.
        // RaskMetaAppDir defaults to `Client`, and this lane cannot use it: half of these creators
        // derive an npm package name from the target directory and reject capitals outright. So the
        // front end lives in `client` and the property says so — otherwise the build looks in one
        // folder while the app is in another, which on Linux is simply a missing front end.
        var appDir = $"\n    <!-- These creators reject a capital letter in the directory name. -->"
                     + $"\n    <RaskMetaAppDir>{framework.AppDir}</RaskMetaAppDir>";

        var litestream = batteries.Data
            ? "\n    <!-- The litestream binary ships in the Docker image, not fetched at build time. -->"
              + "\n    <RaskLitestreamDownload>false</RaskLitestreamDownload>"
            : "";

        return $"""
        <Project Sdk="Microsoft.NET.Sdk.Web">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>

            <!--
              The one property that turns this lane on. It is named here rather than passed to
              AddRaskMeta() because the BUILD needs it anyway — to install, build and publish the right
              front end — and baking it into the assembly is what lets AddRaskMeta() take no argument and
              still be certain it is fronting the framework that was actually built.
            -->
            <RaskMetaFramework>{framework.Key}</RaskMetaFramework>{appDir}{litestream}
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Rask.Cqrs" Version="{version}"/>
            <PackageReference Include="Rask.Cqrs.Server" Version="{version}"/>
            <!--
              Runs the front end's own toolchain during `dotnet build` (npm ci, npm run build), copies its
              output on publish, and supervises `node` beside Kestrel at runtime. Your C# message records
              are projected into TypeScript on every build, into {framework.GeneratedDir}/.
              -p:RaskMetaBuild=false skips node entirely — the app still compiles and its API still works.
            -->
            <PackageReference Include="Rask.Meta.Hosting" Version="{version}"/>{refs}
          </ItemGroup>

        </Project>

        """;
    }

    /// <summary>
    ///     The packages the host needs. The first three are written into the csproj by hand (with their
    ///     own comments), so the caller skips them when appending the rest.
    /// </summary>
    private static List<string> MetaServerPackages(ServerBatteries batteries)
    {
        // Bootstrap is a C#-component library and this host renders no components — the front end owns
        // every pixel. Cqrs is cleared because Rask.Cqrs.Server supersedes it, and both are listed by
        // hand above.
        var packages = ServerPackages(batteries with { Cqrs = false });
        packages.Remove("Rask.Server");
        packages.Remove("Rask.Cqrs");

        packages.Insert(0, "Rask.Meta.Hosting");
        packages.Insert(0, "Rask.Cqrs.Server");
        packages.Insert(0, "Rask.Cqrs");
        return packages;
    }

    private static string MetaProgram(ServerBatteries batteries)
    {
        var sb = new StringBuilder();
        if (batteries.Data)
        {
            sb.Append($"using {NameToken}.Features.Shared;\n");
        }

        sb.Append("using Rask.Cqrs.Server;\n");
        sb.Append("using Rask.Meta.Hosting;\n");
        sb.Append(DatabaseAndBatteryUsings(batteries));

        sb.Append("""

            var builder = WebApplication.CreateBuilder(args);

            // AddRaskCqrsServer registers the mediator AND the endpoint pair the front end dispatches
            // through. The TypeScript the front end imports is generated from these same message records
            // at build time, so the two halves cannot disagree about a payload or a result.
            //
            // RequireAuthenticatedUser is OFF because this template has no authentication to require —
            // left on, every message would answer 401 and nothing would work. Add a cookie or JWT scheme
            // and DELETE this argument: the default is on for a reason.
            builder.Services.AddRaskCqrsServer(o => o.RequireAuthenticatedUser = false);

            builder.Services.AddSingleton<Company.RaskServer.Features.Hello.VisitCounter>();

            // A liveness/readiness endpoint (mapped below). `rask deploy` probes it to gate the
            // blue-green swap; also useful for any load balancer or orchestrator.
            builder.Services.AddHealthChecks();

            // The front end's Node server, supervised as a child process on loopback. The framework is
            // the one the .csproj named — read from the assembly, so the build and the running host
            // cannot disagree about which one was built.
            //
            // Set o.BaseUrl to this host's own loopback address if you dispatch from a SERVER render: a
            // relative URL has no origin in Node, and the value is deliberately configured rather than
            // derived from the incoming request.
            builder.Services.AddRaskMeta();

            """.TrimStart('\n'));

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
            // Mapped BEFORE UseRaskMeta. That call ends the pipeline with a fallback that forwards to the
            // front end, so an endpoint added after it would be answered with a rendered page instead —
            // which is the one failure of this lane that looks like a front-end bug.
            app.MapRaskCqrs();

            app.MapHealthChecks("/healthz");
            """);

        Block(sb, """
            // Serves the framework's built client assets from Kestrel (one hop less per asset, and the
            // immutable cache headers written for you) and forwards everything else to the node process.
            // Before the port answers, requests get 503 with Retry-After rather than a 502 from
            // forwarding into a closed socket.
            app.UseRaskMeta();

            app.Run();
            """);

        return sb.ToString();
    }
}
