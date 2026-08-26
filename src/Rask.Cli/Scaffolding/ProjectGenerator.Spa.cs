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
            ($"{NameToken}.Server/{NameToken}.Server.csproj", SpaServerCsproj(batteries, version)),
            ($"{NameToken}.Server/Program.cs", SpaServerProgram(batteries)),
            ($"{NameToken}.Server/Features/Hello/Messages.cs", SpaMessages),
            ($"{NameToken}.Server/Features/Hello/HelloHandlers.cs", SpaHandlers),
            ($"{NameToken}.Server/Properties/launchSettings.json", SpaLaunchSettings),
            ($"{NameToken}.Server/appsettings.json", AppSettings),
            ($"{NameToken}.Server/appsettings.Production.json", AppSettingsProduction),

            // The overlay: everything else in the client is create-vite's.
            ($"{NameToken}.Client/vite.config.ts", SpaViteConfig(framework)),
            ($"{NameToken}.Client/src/App.tsx", SpaApp),
            ($"{NameToken}.Client/src/main.tsx", SpaMain),
            ("README.md", SpaReadme(framework)),
        };

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
                    ["--yes", "create-vite@latest", name + ".Client", "--template", framework.ViteTemplate],
                    $"Scaffolding the {framework.DisplayName} client with create-vite…",
                    "Install Node.js 20.19 or newer from https://nodejs.org "
                    + "(macOS: brew install node; Windows: winget install OpenJS.NodeJS.LTS; "
                    + "Linux: your distro's nodejs package)."),
            ],
            Patches =
            [
                new ScaffoldPatch(
                    System.IO.Path.Combine(client, "package.json"),
                    AddQueryDependency,
                    "adding @tanstack/react-query"),
                new ScaffoldPatch(
                    System.IO.Path.Combine(client, ".gitignore"),
                    IgnoreGeneratedContracts,
                    "ignoring the generated contracts"),
            ],
        };
    }

    /// <summary>
    ///     Adds TanStack Query to whatever <c>create-vite</c> wrote, leaving the rest of the file alone.
    /// </summary>
    /// <remarks>
    ///     Parsed and re-serialised rather than string-spliced: the scaffolder's <c>package.json</c> is not
    ///     ours and its shape is free to change. Idempotent, so re-running <c>rask new --force</c> over an
    ///     existing client does not produce a duplicate key or a second copy of the range.
    /// </remarks>
    internal static string AddQueryDependency(string packageJson)
    {
        var root = JsonNode.Parse(packageJson) as JsonObject
                   ?? throw new InvalidOperationException("package.json is not a JSON object.");

        if (root["dependencies"] is not JsonObject dependencies)
        {
            dependencies = [];
            root["dependencies"] = dependencies;
        }

        dependencies["@tanstack/react-query"] = TanStackQueryRange;

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>
    ///     The TanStack Query range the template pins.
    /// </summary>
    /// <remarks>
    ///     A caret range rather than an exact version: the client's own lockfile is what makes a build
    ///     reproducible, and pinning exactly here would freeze every scaffolded app on whatever was current
    ///     the day its Rask was released.
    /// </remarks>
    private const string TanStackQueryRange = "^5.100.0";

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

    private static string SpaServerCsproj(ServerBatteries batteries, string version)
    {
        var refs = new StringBuilder();

        // Skip(3): Rask.Cqrs, Rask.Cqrs.Server and Rask.Spa.Hosting are written below by hand, each with
        // the comment explaining what it brings.
        foreach (var package in SpaServerPackages(batteries).Skip(3))
        {
            refs.Append($"\n    <PackageReference Include=\"{package}\" Version=\"{version}\"/>");
        }

        var litestream = batteries.Data
            ? "\n    <!-- The litestream binary ships in the Docker image, not fetched at build time. -->"
              + "\n    <RaskLitestreamDownload>false</RaskLitestreamDownload>"
            : "";

        return $"""
        <Project Sdk="Microsoft.NET.Sdk.Web">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>{litestream}
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
        var packages = ServerPackages(batteries with { Bootstrap = false, Cqrs = false });
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

    private static string SpaViteConfig(SpaFramework framework) =>
        $$"""
        import {{framework.PluginImport}}
        import { defineConfig } from 'vite'

        // https://vite.dev/config/
        export default defineConfig({
          plugins: [{{framework.PluginCall}}],
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

    private const string SpaMain =
        """
        import { StrictMode } from 'react'
        import { createRoot } from 'react-dom/client'
        import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
        import './index.css'
        import App from './App.tsx'

        // Defaults left alone deliberately: staleTime 0 and a 5-minute garbage-collection window are
        // TanStack's, and Rask's own C# query client mirrors them, so the two halves of an app behave the
        // same way. Change them here once you know which way you want them to differ.
        const queryClient = new QueryClient()

        createRoot(document.getElementById('root')!).render(
          <StrictMode>
            <QueryClientProvider client={queryClient}>
              <App />
            </QueryClientProvider>
          </StrictMode>,
        )

        """;

    private const string SpaApp =
        """
        import { useState } from 'react'
        import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
        import { raskMutation, raskQuery } from './rask/query'
        import { getGreeting, recordVisit } from './rask/messages'
        import './App.css'

        export default function App() {
          const [name, setName] = useState('world')
          const queryClient = useQueryClient()

          // The message carries its own result type, so `greeting` is a Greeting with no cast and no
          // wire name spelled out here. Renaming a property in the C# record breaks this line at build
          // time rather than at runtime.
          const { data: greeting, isPending, error } = useQuery(raskQuery(getGreeting({ name })))

          const visit = useMutation({
            ...raskMutation(recordVisit),
            // The factory carries its wire name, so invalidation is never a string literal either.
            onSuccess: () => queryClient.invalidateQueries({ queryKey: [getGreeting.messageName] }),
          })

          return (
            <main>
              <h1>Rask + React</h1>

              <label>
                Name{' '}
                <input value={name} onChange={(event) => setName(event.target.value)} />
              </label>

              {isPending && <p>Loading…</p>}
              {error && <p role="alert">{error.message}</p>}

              {greeting && (
                <>
                  <p>{greeting.message}</p>
                  {/* seenAt is a real Date, revived from the wire because the C# type said it was an
                      instant — not because the string looked like one. Formatting is the browser's job:
                      `undefined` means the visitor's own locale, and their own time zone. */}
                  <p>
                    Server time:{' '}
                    {new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(greeting.seenAt)}
                  </p>
                  <p>Visits: {greeting.visits}</p>
                </>
              )}

              <button onClick={() => visit.mutate({ name })} disabled={visit.isPending}>
                Record a visit
              </button>
            </main>
          )
        }

        """;

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
    string PluginCall)
{
    public static readonly SpaFramework React = new(
        "react",
        "React",
        "react-ts",
        "react from '@vitejs/plugin-react'",
        "react()");
}
