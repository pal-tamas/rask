using System.Text;

namespace Rask.Cli.Scaffolding;

// The server template: an ASP.NET live-server Rask app.
internal static partial class ProjectGenerator
{
    /// <summary>Generates the <c>server</c> template into <paramref name="targetDirectory"/>.</summary>
    public static ScaffoldResult GenerateServer(string targetDirectory, string name, ServerBatteries batteries, string version)
    {
        ArgumentNullException.ThrowIfNull(batteries);

        // Apply the flags' implications once, up front, so every branch below reads the resolved set
        // (--jobs means --data means --cqrs, --push means --pwa, …). See ServerBatteries.Normalized.
        batteries = batteries.Normalized();

        var files = new List<(string Path, string Content)>
        {
            ($"{NameToken}.csproj", ServerCsproj(batteries, version)),
            ("Program.cs", ServerProgram(batteries)),
            ("Features/Shared/App.cs", AppShellCs(batteries.Styling)),
            ("Features/Home/HomePage.cs", HomePageCs(batteries.Styling)),
            ("Properties/launchSettings.json", LaunchSettings),
            ("appsettings.json", AppSettings),
            ("tsconfig.json", TsConfigJson),
            ("appsettings.Production.json", AppSettingsProduction),
        };

        if (batteries.Auth)
        {
            files.Add(("Features/Auth/CredentialStore.cs", AuthCredentialStore));
            files.Add(("Features/Auth/LoginPage.cs", AuthLoginPage));
            files.Add(("Features/Auth/MembersPage.cs", AuthMembersPage));
        }

        if (batteries.Data)
        {
            files.Add(("Features/Shared/AppDbContext.cs", AppDbContextCs(batteries)));
        }

        if (batteries.Push)
        {
            files.Add(("Features/Push/PushSubscriptions.cs", PushSubscriptionsCs));
        }

        if (batteries.Tailwind)
        {
            files.Add(("Styles/app.css", TailwindInputCss));
        }

        files.Add(("Features/Shared/ErrorPage.cs", ErrorPageCs(batteries.Styling)));

        if (batteries.Pwa)
        {
            files.Add(("wwwroot/icon.svg", IconSvg));
            files.Add(("wwwroot/offline.html", OfflineHtml));
        }

        if (batteries.Localization)
        {
            files.AddRange(StringCatalogs([.. batteries.Cultures]));
        }

        if (batteries.Docker)
        {
            files.Add(("Dockerfile", Dockerfile(batteries)));
            files.Add((".dockerignore", DockerIgnore));
        }

        files.AddRange(ProjectHygiene($"{NameToken}.csproj"));

        var scaffoldFiles = Materialize(targetDirectory, name, files);

        return new ScaffoldResult(scaffoldFiles, ServerNextSteps(name, batteries))
        {
            Packages = ServerPackages(batteries),
        };
    }

    // The package list, in the same order the csproj emits them, so `rask new`'s summary matches the file.
    private static List<string> ServerPackages(ServerBatteries batteries)
    {
        var packages = new List<string> { "Rask.Server" };
        if (batteries.Bootstrap)
        {
            packages.Add("Rask.Bootstrap");
        }

        if (batteries.Tailwind)
        {
            packages.Add("Rask.Tailwind");
        }

        if (batteries.Cqrs)
        {
            packages.Add("Rask.Cqrs");

            // Not a flag of its own. A dispatcher without a cache means every render refetches, and the
            // first thing anyone building a page over IDispatcher needs is the thing that stops that —
            // so it arrives wired rather than as something to discover in the docs later.
            packages.Add("Rask.Query");
        }

        if (batteries.Data)
        {
            packages.Add("Rask.Data");
            packages.Add("Rask.SQLite.EntityFrameworkCore");

            // Continuous backup. Referenced whenever there's a database: the wiring in Program.cs stays
            // inert until Litestream:ReplicaUrl is set, so this costs an unused reference and buys a
            // one-env-var path from "single copy on one disk" to "the box is disposable".
            packages.Add("Rask.SQLite.Litestream");
        }

        if (batteries.Outbox)
        {
            packages.Add("Rask.Outbox");
        }

        if (batteries.Jobs)
        {
            packages.Add("Rask.Jobs");
        }

        if (batteries.Mail)
        {
            packages.Add("Rask.Mail");
        }

        if (batteries.Cache)
        {
            packages.Add("Rask.Cache");
        }

        if (batteries.AnySqliteOps)
        {
            packages.Add("Rask.SQLite.Snapshots");
        }

        if (batteries.Logs)
        {
            packages.Add("Rask.Logging");
        }

        if (batteries.Push)
        {
            packages.Add("Rask.WebPush");
        }

        if (batteries.Ops)
        {
            packages.Add("Rask.Dashboard");
        }

        if (batteries.Wasm)
        {
            packages.Add("Rask.Wasm.Hosting");
        }

        return packages;
    }

    private static string ServerCsproj(ServerBatteries batteries, string version)
    {
        // Rask.SQLite.EntityFrameworkCore brings the UseRaskSqlite extension and pulls its EF Core provider
        // transitively; `rask db` adds EF's Design package on the first migration, so the base app builds
        // and runs with no design-time dependency.
        var refs = new StringBuilder();
        foreach (var package in ServerPackages(batteries).Skip(1))
        {
            refs.Append($"\n    <PackageReference Include=\"{package}\" Version=\"{version}\"/>");
        }

        // Rask.SQLite.Litestream's build props download the litestream binary from GitHub releases unless
        // told not to, so without this a scaffolded app can't be built offline — and errors outright on a
        // RID with no published asset. The binary belongs on the server (--docker copies it into the
        // image), not in everyone's build.
        // The one-project build. `dotnet publish` generates a second project into obj/ carrying the
        // WebAssembly SDK, compiles this app's own sources into it, and publishes the bundle into
        // wwwroot. `dotnet run` is untouched -- a bundle takes minutes to link and buys nothing in
        // development, where the page is server-live and hot-reloaded.
        var browserRungProperty = batteries.Wasm
            ? "\n    <!-- Publish a browser bundle from this same project: see docs/render-modes.md. -->"
              + "\n    <RaskBrowserRung>true</RaskBrowserRung>"
              // Stated rather than left to the default. The default is {RootNamespace}.App, which is
              // where a hand-written app puts its root; this layout puts cross-cutting code in
              // Features/Shared, so the generated browser entry point would name a type that is not there
              // and fail to compile inside a nested publish -- an error a long way from anything the
              // author wrote.
              + "\n    <RaskBrowserRootComponent>$(RootNamespace).Features.Shared.App</RaskBrowserRootComponent>"
            : "";

        var litestreamProperty = batteries.Data
            ? "\n    <!-- The litestream binary ships in the Docker image, not fetched at build time. -->"
              + "\n    <RaskLitestreamDownload>false</RaskLitestreamDownload>"
            : "";

        return $"""
        <Project Sdk="Microsoft.NET.Sdk.Web">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>{browserRungProperty}{litestreamProperty}
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Rask.Server" Version="{version}"/>{refs}
          </ItemGroup>

        </Project>

        """;
    }

    // Appends one wiring block followed by a blank line. With every battery enabled Program.cs is a dozen
    // commented registrations; without the separator they run together into one wall of text.
    private static void Block(StringBuilder target, string block) =>
        target.Append(block.Trim('\n')).Append("\n\n");

    private static string ServerProgram(ServerBatteries batteries)
    {
        var sb = new StringBuilder();
        // App (and, with --data, AppDbContext) live in the Features/Shared bucket.
        sb.Append("using Company.RaskServer.Features.Shared;\nusing Microsoft.AspNetCore.HttpOverrides;\nusing Rask.Server;\nusing Rask.Server.Diagnostics;\n");
        if (batteries.Auth)
        {
            sb.Append("using Company.RaskServer.Features.Auth;\n");
            sb.Append("using Microsoft.AspNetCore.Authentication.Cookies;\n");
        }

        if (batteries.Push)
        {
            sb.Append("using Company.RaskServer.Features.Push;\n");
            sb.Append("using Rask.WebPush;\n");
        }

        if (batteries.Cqrs)
        {
            sb.Append("using Rask.Query;\n");
        }

        if (batteries.Pwa)
        {
            sb.Append("using Rask.Core.Browser;\n");
        }

        if (batteries.Wasm)
        {
            sb.Append("using Rask.Wasm.Hosting;\n");
        }

        sb.Append(DatabaseAndBatteryUsings(batteries));

        sb.Append("\nvar builder = WebApplication.CreateBuilder(args);\n\n");
        if (batteries.Localization)
        {
            // Configured on the EXISTING AddRask call rather than a second one. A second
            // AddRask(configureCulture: ...) compiles and reads correctly, but the options are
            // registered with TryAddSingleton, so the first (empty) registration wins and the app
            // silently ships with no languages at all.
            var languages = string.Join(", ", batteries.Cultures.Select(c => $"\"{c}\""));
            // Configured on the SAME call for the same reason the cultures are: the options are
            // registered with TryAddSingleton, so a second AddRask would be dropped on the floor.
            var browserRung = batteries.Wasm ? "configureServer: o => o.RenderModes.Wasm = true, " : "";
            sb.Append($$"""
                // The languages this app ships. The FIRST is the default a visitor falls back to when
                // nothing else matches. Their language is negotiated per request -- ?culture= beats a
                // remembered cookie, which beats the browser's Accept-Language -- and then belongs to
                // their session, so it survives every render over the live socket.
                //
                // Text comes from Resources/Strings.{culture}.json, compiled into typed members: a
                // missing key is a build error rather than a blank on the page (docs/diagnostics.md).
                builder.Services.AddRask({{browserRung}}configureCulture: c =>
                {
                    foreach (var language in new[] { {{languages}} })
                    {
                        c.SupportedCultures.Add(language);
                    }
                });

                """);
        }
        else if (batteries.Wasm)
        {
            sb.Append("builder.Services.AddRask(configureServer: o => o.RenderModes.Wasm = true);\n");
        }
        else
        {
            sb.Append("builder.Services.AddRask();\n");
        }

        if (batteries.Wasm)
        {
            sb.Append("""

                // Serves the browser bundle this project publishes into wwwroot. Registered here and
                // mapped below, before UseRouting.
                builder.Services.AddRaskWasmHost();

                """.TrimStart('\n'));
        }
        sb.Append("""

            // A liveness/readiness endpoint (mapped below) — `rask deploy` probes it to gate the blue-green
            // swap, and any load balancer or orchestrator can use it too. AddRaskLiveSessions reports the
            // live-session pool: Degraded at 80% of MaxSessions, Unhealthy once new sessions are being
            // refused with 503 — so a host that is full says so instead of answering a bare "up". Add real
            // dependency checks alongside it, e.g. .AddDbContextCheck<AppDbContext>().
            builder.Services.AddHealthChecks().AddRaskLiveSessions();

            // Behind a reverse proxy (`rask deploy` runs Caddy in front), the app sees the proxy's own
            // address and a plain-HTTP request. Without this Request.Scheme is "http", so UseHsts never
            // emits, RemoteIpAddress is the proxy rather than the visitor, and any redirect you build is
            // wrong. The proxy's container IP is assigned by Docker and changes, so it can't be named in
            // KnownProxies — clearing the lists is what makes this work, and it is safe in that topology
            // because the container publishes no host port: only the proxy can reach it. If you expose this
            // app directly to the internet, delete this block (a client could otherwise forge its own IP).
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });

            """.TrimStart('\n'));


        if (batteries.Cqrs)
        {
            sb.Append("""

                // CQRS mediator: one call registers every IQueryHandler/ICommandHandler/INotificationHandler in
                // this assembly (source-generated, reflection-free — trim/AOT-safe). Inject IDispatcher to send
                // messages; add pipeline behaviors with o.AddOpenBehavior(...). See docs/cqrs.md.
                builder.Services.AddRaskCqrs();

                // Server state over that dispatcher: dedup, staleness, background refetch, invalidation.
                // Inject IQueryClient and render a Query<T> instead of fetching in OnInitializedAsync —
                // scoped per live session, so one visitor's results are never handed to another.
                // See docs/query.md.
                builder.Services.AddRaskQuery();

                """.TrimStart('\n'));
        }

        // The database and every DB-backed battery, shared verbatim with the wasm-hosted template:
        // that template's .Server host wires the same AppDbContext and the same AddRaskX<AppDbContext>()
        // calls, and a second copy of these blocks would drift the moment one of them was corrected.
        AppendDatabaseAndBatteries(sb, batteries);

        if (batteries.Pwa)
        {
            sb.Append("""

                // Installable PWA: AddRaskPwa serves the manifest + service worker and emits the manifest link +
                // SW registration into the server-rendered <head>. The app is installable and push-capable, but NOT
                // an offline app (a Server app renders over a live WebSocket) — offline navigations show wwwroot/
                // offline.html. To send Web Push from this app, add Rask.WebPush; see docs/pwa.md.
                builder.Services.AddRaskPwa(new WebAppManifest
                {
                    Name = "Rask App",
                    ShortName = "Rask App",
                    ThemeColor = "#512BD4",
                    BackgroundColor = "#faf9fe",
                    Display = DisplayMode.Standalone,
                    Icons = [new ManifestIcon("icon.svg", "any", "image/svg+xml", "any maskable")]
                });

                """.TrimStart('\n'));
        }

        if (batteries.Auth)
        {
            sb.Append("""

                // Cookie auth — Rask reads HttpContext.User; the sign-in handshake sets this cookie on redeem.
                builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(o =>
                    {
                        o.Cookie.Name = "rask.auth";
                        // Secure-by-default: never send the auth cookie over plain HTTP, and use SameSite=Lax so it
                        // doesn't ride cross-site POSTs (CSRF). The dev launch profile runs on HTTPS so the cookie
                        // is set in development too; if you must serve over plain HTTP, relax SecurePolicy.
                        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                        // Fully qualified: the --pwa `using Rask.Core.Browser` also defines a SameSiteMode.
                        o.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                        o.LoginPath = "/login";
                        o.AccessDeniedPath = "/forbidden";
                    });
                builder.Services.AddSingleton<ICredentialStore, DemoCredentialStore>();

                """.TrimStart('\n'));
        }

        sb.Append("""

            var app = builder.Build();

            // FIRST: rewrite Request.Scheme/RemoteIpAddress from the proxy's headers, so everything below
            // (HSTS, redirects, your own logging) sees the request the visitor actually made.
            app.UseForwardedHeaders();

            // Health endpoint next — as terminal middleware it short-circuits before UseHttpsRedirection,
            // so /health answers 200 over plain HTTP. `rask deploy` probes it internally on http://…:8080
            // (no X-Forwarded-Proto), where a redirected endpoint would 307 to a port nothing listens on.
            app.UseHealthChecks("/health");

            // Unhandled exceptions. ErrorBoundary already covers anything thrown inside a component tree;
            // this catches everything outside it, which would otherwise be a bare 500 with an empty body.
            // Non-Development only — the developer exception page is strictly more useful locally, and this
            // one deliberately shows nothing about the exception.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/error");
            }

            // Transport security (applies whether or not auth is enabled): redirect HTTP→HTTPS, and in
            // non-Development emit HSTS so browsers refuse plain-HTTP for the configured max-age.
            if (!app.Environment.IsDevelopment())
            {
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.MapStaticAssets();

            // Give bare status codes (a 404 from an unmatched route) a readable body instead of a blank page.
            app.UseStatusCodePages();

            """.TrimStart('\n'));

        if (batteries.Data)
        {
            sb.Append("""

                // Restore before anything opens the database (migrations, the first query). On a box that
                // already has app.db this is a no-op and never clobbers it; on a fresh one it pulls the
                // database back from the replica — which is the moment the "disposable box" promise is kept
                // or broken. Guarded because RestoreSqliteFromLitestreamAsync throws when no replica is
                // configured, and an app without one must still start.
                if (!string.IsNullOrWhiteSpace(replicaUrl))
                {
                    await app.Services.RestoreSqliteFromLitestreamAsync();
                }

                """.TrimStart('\n'));
        }

        if (batteries.Auth)
        {
            sb.Append("// Must precede UseRask so HttpContext.User is populated on the GET and the WS upgrade.\n");
            sb.Append("app.UseAuthentication();\n");
            sb.Append("app.UseAuthorization();\n\n");
        }

        if (batteries.Push)
        {
            sb.Append("// Mapped before UseRask: its catch-all serves the SPA for anything unmatched, so a minimal API\n");
            sb.Append("// registered after it would never be reached.\n");
            sb.Append("app.MapPushSubscriptions();\n\n");
        }

        if (batteries.Wasm)
        {
            sb.Append("""
                // The browser bundle, served from this app's own wwwroot.
                //
                // BEFORE UseRouting, and UseRouting written out rather than left implicit -- both matter.
                // Routing selects an endpoint before the static-file middleware runs, and that middleware
                // steps aside when one is already selected, so mapping the bundle afterwards lets the
                // Rask catch-all answer /_framework/*.wasm with text/html -- which the browser reports as
                // a broken WebAssembly module, nowhere near the ordering that caused it. And
                // WebApplication inserts UseRouting at the START of the pipeline when nobody calls it,
                // which would put routing ahead of this line however early it appears.
                app.UseRaskWasmAssets();
                app.UseRouting();

                """.TrimStart('\n'));
        }

        sb.Append("""
            // To host this app under a sub-path (e.g. behind a reverse proxy mapping
            // /myapp/* → this server), pass pathBase. Every framework endpoint and
            // emitted URL is scoped under the prefix; user-space routes stay unprefixed.
            //   app.UseRask<App>(pathBase: "/myapp");
            app.UseRask<App>();

            app.Run();

            """.TrimStart('\n'));

        return sb.ToString();
    }

    /// <summary>
    /// The app's configuration file. Scaffolded (rather than left to the reader) because
    /// <see href="https://learn.microsoft.com/aspnet/core/fundamentals/configuration/">configuration</see>
    /// is where Rask's own diagnostics are tuned — <c>docs/observability.md</c> tells you to set
    /// <c>Logging:LogLevel:Rask.Live</c>, and without this file there is nowhere to put it.
    /// </summary>
    private const string AppSettings =
        """
        {
          "Logging": {
            "LogLevel": {
              "Default": "Information",
              "Microsoft.AspNetCore": "Warning",

              // Rask reports framework faults through these categories — a lifecycle hook that threw with
              // no ErrorBoundary above it, a duplicate sibling Key, a handler fault, a rejected WebSocket
              // frame. See docs/observability.md for the full table.
              "Rask.Lifecycle": "Warning",
              "Rask.Live": "Warning",
              "Rask.Diff": "Warning"
            }
          },
          "AllowedHosts": "*"
        }

        """;

    /// <summary>
    /// Production overrides. Kept separate so a value you change for the live site can't quietly change
    /// how the app behaves on your machine. `rask deploy` runs the container with
    /// <c>ASPNETCORE_ENVIRONMENT=Production</c>, which is what selects this file.
    /// </summary>
    private const string AppSettingsProduction =
        """
        {
          "Logging": {
            "LogLevel": {
              // Quieter than development: request logging on a live site is mostly noise, and the
              // Rask.Server meter + activity source carry the operational signal instead.
              "Default": "Warning",
              "Microsoft.AspNetCore": "Error",
              // Kept at Information deliberately: these are the start/stop lines, and knowing exactly
              // when the app last restarted is one of the things a durable log (--logs) exists to
              // answer. Two lines per lifetime is not noise.
              "Microsoft.Hosting.Lifetime": "Information"
            }
          }

          // Secrets do NOT belong here — this file is committed. Pass them at deploy time with
          // `rask deploy --env KEY=VALUE` / `--env-file`, which become environment variables and
          // override anything set here (ConnectionStrings__App, for example).
        }

        """;

    private static string ServerNextSteps(string name, ServerBatteries batteries)
    {
        var steps = new StringBuilder();
        steps.Append("Created ").Append(name).Append(" (Rask server app).\n\nNext steps:\n");
        steps.Append("  cd ").Append(name).Append('\n');
        steps.Append("  rask dev            # run with hot reload (or: dotnet run)\n");
        if (batteries.Docker)
        {
            steps.Append("  docker build -t ").Append(name.ToLowerInvariant()).Append(" .   # then: docker run -p 8080:8080 …\n");
        }

        // Nothing about migrations here any more. `rask new` creates and applies the first one itself, so
        // by the time this text is printed the tables the pillars need already exist — and repeating the
        // commands would read as work still to do. The command prints the manual pair only in the two
        // cases where it could not run them: --no-restore, and a migration that failed.
        if (batteries.Data)
        {
            steps.Append("\nThe first migration is already applied to app.db. Add a DbSet<T> to AppDbContext\n");
            steps.Append("for your first entity, then `rask db add <Name>` and `rask db update` to migrate it.\n");
        }

        if (batteries.Push)
        {
            steps.Append("\nWeb Push needs a VAPID key pair. Generate one and save it to user-secrets:\n");
            steps.Append("  dotnet user-secrets set \"WebPush:PublicKey\" \"<public>\"\n");
            steps.Append("  dotnet user-secrets set \"WebPush:PrivateKey\" \"<private>\"\n");
            steps.Append("  (VapidKeys.Generate() prints a pair; the private key must never be served.)\n");
        }

        return steps.ToString();
    }

    // ---- --data template files ----

    // An empty database ready for features. Add one `DbSet<T>` per entity; ApplyRaskConventions +
    // ApplyConfigurationsFromAssembly pick up each feature's IEntityTypeConfiguration automatically, so the
    // context needs no per-entity edits beyond that line.
    private static string AppDbContextCs(ServerBatteries batteries)
    {
        var usings = new StringBuilder("using Microsoft.EntityFrameworkCore;\nusing Rask.Data;\n");
        var schema = new StringBuilder();

        // Each pillar owns a table (or two) in the app's own database. These calls only add the framework
        // entities to the model; `rask db add` then writes the migration that creates them.
        if (batteries.Outbox)
        {
            usings.Append("using Rask.Outbox;\n");
            schema.Append("\n        modelBuilder.AddRaskOutbox();");
        }

        if (batteries.Jobs)
        {
            usings.Append("using Rask.Jobs;\n");
            schema.Append("\n        modelBuilder.AddRaskJobs();");
        }

        if (batteries.Mail)
        {
            usings.Append("using Rask.Mail;\n");
            schema.Append("\n        modelBuilder.AddRaskMail();");
        }

        if (batteries.Cache)
        {
            usings.Append("using Rask.Cache;\n");
            schema.Append("\n        modelBuilder.AddRaskCache();");
        }

        return $$"""
        {{usings.ToString().TrimEnd('\n')}}

        namespace Company.RaskServer.Features.Shared;

        public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                // ApplyRaskConventions walks the model as it stands, applying the soft-delete query filter and
                // the concurrency token to whatever is already in it — so it has to follow the configurations,
                // not precede them, or entities registered afterwards silently miss out.
                modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
                modelBuilder.ApplyRaskConventions();{{schema}}
            }
        }

        """;
    }

    // The production error page. UseExceptionHandler re-executes the pipeline at this route, so it renders
    // through the app shell like any other page rather than looking like a framework error.
    // The error page's body, in Bs* components or plain elements — the only part of the page that
    // depends on whether the project took the component library.
    private static string ErrorPageCs(Styling styling) =>
        ErrorPageTemplate.Replace(
            "{{body}}",
            // Tailwind shares the plain body: it is a handful of elements with no component library
            // behind it either way, and a second copy would be two things to keep saying the same.
            styling == Styling.Bootstrap ? ErrorPageBootstrapBody : ErrorPageBaselineBody,
            StringComparison.Ordinal);

    private const string ErrorPageTemplate =
        """
        using System.Diagnostics;
        using Microsoft.AspNetCore.Authorization;
        using Rask.Core.Routing;

        // The generated Routes class is per-namespace, and this page lives in Features.Shared while the
        // home page lives in Features.Home — alias it rather than fully qualifying at the call site.
        using HomeRoutes = Company.RaskServer.Features.Home.Routes;

        namespace Company.RaskServer.Features.Shared;

        // [AllowAnonymous] because an error page that redirects to /login is worse than the error: if you
        // later add a fallback authorization policy, this route must stay reachable.
        [AllowAnonymous]
        [Route("/error")]
        public sealed partial class ErrorPage : Component
        {
            protected override Component? HeadAssets => [Title["Something went wrong"]];

            protected override Component? Render() =>
        {{body}}
        }

        """;

    // The correlation id is the only detail either body renders — and the comment saying why travels with
    // the generated code, because that is where someone is standing when they consider adding more.
    private static readonly string ErrorPageBootstrapBody =
        """
                Div.Class("mx-auto my-5").Style("max-width:540px")[
                    BsCard.Class("shadow-sm")[
                        BsCardBody[
                            BsCardTitle["Something went wrong"],
                            BsCardText.Class("text-body-secondary")[
                                "The request couldn't be completed. The error has been logged."
                            ],
                            // The correlation id, and deliberately nothing else. Never render the
                            // exception, its message, or a stack trace here — this page is served to
                            // whoever hit the error, and the detail already went to ILogger where you can
                            // match it by this id.
                            Activity.Current?.Id is { Length: > 0 } traceId
                                ? P.Class("mb-3 small text-body-secondary")[
                                    "Reference: ",
                                    Code[traceId]
                                ]
                                : null,
                            NavLink.Href(HomeRoutes.HomePage()).Class("btn btn-primary")["Back to the app"]
                        ]
                    ]
                ];
        """.Trim('\n');

    private static readonly string ErrorPageBaselineBody =
        """
                Main[
                    Div.Class("card")[
                        H1["Something went wrong"],
                        P["The request couldn't be completed. The error has been logged."],
                        // The correlation id, and deliberately nothing else. Never render the exception,
                        // its message, or a stack trace here — this page is served to whoever hit the
                        // error, and the detail already went to ILogger where you can match it by this id.
                        Activity.Current?.Id is { Length: > 0 } traceId
                            ? P.Class("small")[
                                "Reference: ",
                                Code[traceId]
                            ]
                            : null,
                        NavLink.Href(HomeRoutes.HomePage())["Back to the app"]
                    ]
                ];
        """.Trim('\n');

    // ---- --push template files ----

    // A subscription store + the two endpoints a browser needs to subscribe and unsubscribe. Kept in-memory
    // so the scaffold has no schema of its own; move it to a table (or a DbSet on AppDbContext) once you want
    // subscriptions to survive a restart.
    private const string PushSubscriptionsCs =
        """
        using System.Collections.Concurrent;
        using Microsoft.Extensions.DependencyInjection;
        using Rask.WebPush;

        namespace Company.RaskServer.Features.Push;

        /// <summary>The browsers currently subscribed to push, keyed by their endpoint URL.</summary>
        public sealed class PushSubscriptionStore
        {
            private readonly ConcurrentDictionary<string, PushSubscription> _subscriptions = new(StringComparer.Ordinal);

            public IReadOnlyCollection<PushSubscription> All => _subscriptions.Values.ToArray();

            public void Add(PushSubscription subscription) => _subscriptions[subscription.Endpoint] = subscription;

            public void Remove(string endpoint) => _subscriptions.TryRemove(endpoint, out _);
        }

        public static class PushEndpoints
        {
            public static IEndpointRouteBuilder MapPushSubscriptions(this IEndpointRouteBuilder endpoints)
            {
                // The PUBLIC key only — the browser passes it to pushManager.subscribe as applicationServerKey.
                // The private key signs the request and must never leave the server.
                //
                // Resolved optionally, because Program.cs only registers Web Push once a key pair is
                // configured: before then this answers with an empty key rather than failing the request,
                // so the page can say "push isn't configured yet" instead of erroring.
                endpoints.MapGet("/_push/key", (IServiceProvider services) =>
                    Results.Json(new
                    {
                        publicKey = services.GetService<WebPushOptions>()?.VapidKeys?.PublicKey ?? "",
                    }));

                endpoints.MapPost("/_push/subscribe", (PushSubscription subscription, PushSubscriptionStore store) =>
                {
                    store.Add(subscription);
                    return Results.NoContent();
                });

                endpoints.MapPost("/_push/unsubscribe", (PushSubscription subscription, PushSubscriptionStore store) =>
                {
                    store.Remove(subscription.Endpoint);
                    return Results.NoContent();
                });

                return endpoints;
            }
        }

        """;

    // ---- server-only template files ----

    private const string AuthLoginPage =
        """
        using System.Security.Claims;
        using Microsoft.AspNetCore.Authentication.Cookies;
        using Microsoft.AspNetCore.Authorization;
        using Rask.Core.Authentication;
        using Rask.Core.Components;
        using Rask.Core.Routing;

        namespace Company.RaskServer.Features.Auth;

        [AllowAnonymous]
        [Route("login")]
        public sealed partial class LoginPage(IAuthSignIn auth, ICredentialStore creds) : Component
        {
            private readonly LoginModel _model = new();
            private string? _error;

            [QueryParam] public string? ReturnUrl { get; set; }

            protected override Component? Render() =>
                Div.Class("welcome-card")[
                    H1["Sign in"],
                    _error is null ? null : Div.Style("color:#b00020")[_error],
                    // Async submit uses the generated OnValidSubmitAsync sibling (like Button's OnClickAsync).
                    Form.Model(_model).OnValidSubmitAsync(SubmitAsync)[
                        Div[Label.For("username")["Username"], Input.Bind(() => _model.Username).Id("username")],
                        Div[Label.For("password")["Password"], Input.Bind(() => _model.Password).Id("password").Type(InputType.Password)],
                        Button.Type("submit")["Sign in"]
                    ],
                    P["Try alice / password (user) or root / password (admin)."]
                ];

            private async Task SubmitAsync(LoginModel m)
            {
                var claims = creds.Validate(m.Username, m.Password);
                if (claims is null)
                {
                    _error = "Invalid username or password.";
                    return;
                }

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await auth.SignInAsync(new ClaimsPrincipal(identity), returnUrl: ReturnUrl ?? "/members");
            }
        }

        """;

    private const string AuthMembersPage =
        """
        using Microsoft.AspNetCore.Authorization;
        using Rask.Core.Authentication;
        using Rask.Core.Components;
        using Rask.Core.Routing;

        namespace Company.RaskServer.Features.Auth;

        // [Authorize] blocks anonymous deep-links (full GET → 302 to /login). The Authorize component gates the
        // content and re-renders when the post-sign-in reconnect re-seeds the principal; the signed-in view lives
        // in its own component that injects IUserProvider, so it reads the freshly-authenticated principal — no
        // manual Changed subscription.
        [Authorize]
        [Route("members")]
        public sealed partial class MembersPage : Component
        {
            protected override Component? Render() =>
                Div.Class("welcome-card")[
                    Authorize
                        .NotAuthorized(P["Please ", NavLink.Href(Routes.LoginPage())["sign in"], "."])[MemberContent]
                ];
        }

        public sealed partial class MemberContent(IAuthSignIn auth, IUserProvider userProvider) : Component
        {
            protected override Component? Render() =>
                [
                    H1[$"Welcome, {userProvider.Current.Identity?.Name}"],
                    Authorize.Roles(["admin"])[
                        Div.Style("color:#7a5c00")["🔑 You have admin access."]],
                    Button.OnClickAsync(() => auth.SignOutAsync(returnUrl: "/login"))["Sign out"]
                ];
        }

        """;

    /// <summary>
    /// The production image. With <c>--data</c> on a file database it also carries the <c>litestream</c>
    /// binary the app's replicator drives: the wiring in Program.cs is inert without it, so shipping one
    /// without the other would mean a "continuous backup" that silently never runs. Copied from Litestream's
    /// own published image — one layer, no package manager, and the right architecture picked by the manifest.
    /// </summary>
    /// <remarks>
    /// Both spliced extras exist because the database is a <em>file on this box</em>, so a client-server
    /// database gets neither. The two gates differ on purpose: the <c>/data</c> mount point tracks whatever
    /// <c>rask deploy</c> mounts — which includes an app that has no <c>--data</c> yet, so adding it later
    /// doesn't need a new Dockerfile — while the binary is only worth carrying once there is a database to
    /// replicate.
    /// </remarks>
    /// <summary>
    /// A starter catalog. The neutral one carries the app's English; a translation starts as a copy so
    /// the keys line up and the build tells you which ones still need doing (RASK052).
    /// </summary>
    private static string StringsCatalog(bool neutral) =>
        neutral
            ? """
              {
                "AppTitle": "Welcome to Rask",
                "Greeting": "Hello, {name}!",
                "Items": { "$plural": "count", "one": "{count} item", "other": "{count} items" }
              }
              """
            : """
              // Translated text for this language. The keys come from the neutral catalog; one that is
              // missing here is a warning (RASK052) and falls back to the neutral text, so a
              // half-finished translation still renders.
              {
                "AppTitle": "Welcome to Rask",
                "Greeting": "Hello, {name}!",
                "Items": { "$plural": "count", "one": "{count} item", "other": "{count} items" }
              }
              """;

    private static string Dockerfile(ServerBatteries batteries)
    {
        return Splice(Splice(DockerfileTemplate, "@@LITESTREAM@@", batteries.Data ? LitestreamLayer : null),
            "@@DATADIR@@", DataDirectoryLayer);

        // Fill the slot, or drop the marker and the line it sits on so an unused slot leaves no stray blank.
        static string Splice(string template, string marker, string? content) =>
            content is null
                ? template.Replace(marker + "\n", string.Empty, StringComparison.Ordinal)
                    .Replace(marker, string.Empty, StringComparison.Ordinal)
                : template.Replace(marker, content, StringComparison.Ordinal);
    }

    // Both layers carry their own leading blank line so a filled slot is separated from what precedes it,
    // and an empty slot collapses cleanly instead of leaving one behind.
    private const string LitestreamLayer =
        "\n# The replicator binary Program.cs drives when Litestream__ReplicaUrl is set (see docs/sqlite.md).\n"
        + "COPY --from=litestream/litestream:0.3.13 /usr/local/bin/litestream /usr/local/bin/litestream\n";

    private const string DataDirectoryLayer =
        "\n# A writable data directory for the SQLite database, owned by the image's non-root runtime user\n"
        + "# ($APP_UID). `rask deploy` mounts a named volume here and points the app at /data/app.db (via\n"
        + "# ConnectionStrings:App), so the database survives container replacement across redeploys. A fresh\n"
        + "# named volume inherits this directory's ownership, so the non-root app can create app.db in it.\n"
        + "USER root\n"
        + "RUN mkdir -p /data && chown $APP_UID:$APP_UID /data\n"
        + "USER $APP_UID";

    private const string DockerfileTemplate =
        """
        # Multi-stage build: compile on the .NET SDK image, run on the smaller aspnet runtime.
        # The aspnet:10.0 image already runs as a non-root user and listens on port 8080
        # (ASPNETCORE_HTTP_PORTS=8080) — no extra hardening needed for a basic deploy.
        FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
        WORKDIR /src

        # Restore first (cached layer): only the csproj invalidates it, so code edits reuse the cache.
        COPY ["Company.RaskServer.csproj", "./"]
        RUN dotnet restore
        COPY . .
        RUN dotnet publish "Company.RaskServer.csproj" -c Release -o /app --no-restore

        FROM mcr.microsoft.com/dotnet/aspnet:10.0
        WORKDIR /app
        COPY --from=build /app .
        @@LITESTREAM@@
        @@DATADIR@@

        EXPOSE 8080
        # The app calls UseHttpsRedirection(); inside the container no HTTPS port is configured,
        # so it no-ops. Terminate TLS at your reverse proxy / ingress and forward plain HTTP to 8080.
        ENTRYPOINT ["dotnet", "Company.RaskServer.dll"]

        """;

    private const string OfflineHtml =
        """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Offline</title>
            <style>
                :root { color-scheme: light dark; }
                body {
                    margin: 0; min-height: 100vh; display: grid; place-items: center;
                    font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
                    background: #faf9fe; color: #1c1c28; text-align: center; padding: 1.5rem;
                }
                @media (prefers-color-scheme: dark) { body { background: #14131c; color: #e8e7f0; } }
                h1 { font-size: 1.5rem; margin: 0 0 .5rem; }
                p { color: #6c6a7d; line-height: 1.5; margin: 0 0 1.25rem; max-width: 28rem; }
                button { font: inherit; padding: .5rem 1.25rem; border: 0; border-radius: .5rem; background: #512BD4; color: #fff; cursor: pointer; }
            </style>
        </head>
        <body>
            <div>
                <h1>You're offline</h1>
                <p>This is a Rask Server app — its live UI runs over a WebSocket, so it needs a connection. Reconnect and you'll pick up where you left off.</p>
                <button onclick="location.reload()">Try again</button>
            </div>
        </body>
        </html>

        """;
}
