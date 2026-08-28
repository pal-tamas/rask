using System.Text;

namespace Rask.Cli.Scaffolding;

// The wasm-hosted template: a browser-WASM client served by an ASP.NET static-file host, plus a shared
// class library — the idiomatic hosted trio, named to the Blazor convention (Client / Server / Shared).
// The Server references the Client cross-TFM (ReferenceOutputAssembly=false) so the Rask.Wasm.Hosting
// targets publish the client's wwwroot and bake it into the host for `app.UseRask()` to serve at runtime.
internal static partial class ProjectGenerator
{
    /// <summary>
    /// Generates the <c>wasm-hosted</c> template into <paramref name="targetDirectory"/>: a three-project
    /// solution (<c>{name}.Client</c> WASM SPA, <c>{name}.Server</c> ASP.NET host, <c>{name}.Shared</c>
    /// contracts). Slim by default (welcome page only); <paramref name="auth"/> adds the cookie login flow.
    /// </summary>
    public static ScaffoldResult GenerateWasmHosted(string targetDirectory, string name, ServerBatteries requested, string version)
    {
        var batteries = requested.Normalized();
        bool auth = batteries.Auth, pwa = batteries.Pwa, docker = batteries.Docker;

        // The CLIENT's styling. The .Server half renders no components of its own — WasmHostedServerPackages
        // forces it to Plain — so this axis belongs to the browser project alone.
        var styling = batteries.Styling;

        // The UI lives in the .Client, so its language does too — the .Server here is a static-file host
        // that renders nothing and has no catalog to carry.
        string[] cultures = batteries.Localization ? [.. batteries.Cultures] : [];

        var files = new List<(string Path, string Content)>
        {
            // Shared — the class library both the Client and the Server reference.
            ($"{NameToken}.Shared/{NameToken}.Shared.csproj", SharedCsproj(batteries.Cqrs, version)),
            ($"{NameToken}.Shared/Contracts.cs", auth ? WasmHostedSharedContractsAuth : WasmHostedSharedContracts),

            // Client — the browser-WASM SPA (shell in Features/Shared, welcome page in Features/Home).
            ($"{NameToken}.Client/{NameToken}.Client.csproj",
                WasmHostedClientCsproj(styling, version, batteries.Cqrs, cultures.Length > 0)),
            ($"{NameToken}.Client/Program.cs", WasmHostedClientProgram(auth, pwa, batteries.Cqrs, cultures)),
            ($"{NameToken}.Client/Features/Shared/App.cs", WasmHostedClientAppShell(styling)),
            ($"{NameToken}.Client/Features/Home/HomePage.cs", WasmHostedClientHomePage(styling)),
            ($"{NameToken}.Client/wwwroot/index.html", WasmIndexHtml(pwa)),
            ($"{NameToken}.Client/runtimeconfig.template.json", WasmRuntimeConfig),

            // Server — the ASP.NET host that serves the baked WASM bundle.
            ($"{NameToken}.Server/{NameToken}.Server.csproj", WasmHostedServerCsproj(batteries, version)),
            ($"{NameToken}.Server/Program.cs", WasmHostedServerProgram(batteries)),
            ($"{NameToken}.Server/Properties/launchSettings.json", WasmHostedServerLaunchSettings),
            ($"{NameToken}.Server/appsettings.json", AppSettings),
            ($"{NameToken}.Server/appsettings.Production.json", AppSettingsProduction),
        };

        if (batteries.Cqrs)
        {
            // The messages go in Shared because both halves must compile the same record: the client
            // dispatches it, the server handles it, and one definition is the whole point. The handler goes
            // in Server and is never compiled into the browser bundle.
            files.Add(($"{NameToken}.Shared/Messages.cs", WasmHostedSharedMessages));
            files.Add(($"{NameToken}.Server/Features/Hello/HelloHandlers.cs", WasmHostedServerHandlers));
            files.Add(($"{NameToken}.Client/Features/Hello/HelloPage.cs", WasmHostedClientHelloPage));
        }

        if (auth)
        {
            files.Add(($"{NameToken}.Client/Features/Auth/Auth.cs", WasmHostedClientAuth));
            files.Add(($"{NameToken}.Client/Features/Auth/LoginPage.cs", WasmHostedClientLoginPage));
            files.Add(($"{NameToken}.Client/Features/Auth/MembersPage.cs", WasmHostedClientMembersPage));
            files.Add(($"{NameToken}.Server/Features/Auth/CredentialStore.cs", WasmHostedServerCredentialStore));
        }

        if (styling == Styling.Tailwind)
        {
            // In the CLIENT project: Tailwind scans the tree it runs in, and the components whose classes
            // it is looking for are the browser half's. Pointed at the .Server project it would find a host
            // that renders no components and emit an almost-empty stylesheet, with no error.
            files.Add(($"{NameToken}.Client/Styles/app.css", TailwindInputCss));
        }
        if (pwa)
        {
            files.Add(($"{NameToken}.Client/wwwroot/icon.svg", IconSvg));
        }

        files.AddRange(StringCatalogs(cultures, $"{NameToken}.Client/"));

        if (batteries.Data)
        {
            // The database lives in the .Server project, not the .Client and not the .Shared: it is the
            // only one of the three that runs on a machine with a disk. The Client reaches it over the
            // host's API, exactly as it already does for auth.
            files.Add(($"{NameToken}.Server/Features/Shared/AppDbContext.cs", WasmHostedServerDbContext(batteries)));
        }

        if (docker)
        {
            files.Add(("Dockerfile", WasmHostedDockerfile));
            files.Add((".dockerignore", DockerIgnore));
        }

        files.AddRange(ProjectHygiene(
            $"{NameToken}.Client/{NameToken}.Client.csproj",
            $"{NameToken}.Server/{NameToken}.Server.csproj",
            $"{NameToken}.Shared/{NameToken}.Shared.csproj"));

        var scaffoldFiles = Materialize(targetDirectory, name, files);

        return new ScaffoldResult(scaffoldFiles, WasmHostedNextSteps(name, docker))
        {
            Packages = styling switch
            {
                Styling.Bootstrap => ["Rask.Wasm", "Rask.Bootstrap", "Rask.Wasm.Hosting"],
                Styling.Tailwind => ["Rask.Wasm", "Rask.Tailwind", "Rask.Wasm.Hosting"],
                _ => ["Rask.Wasm", "Rask.Wasm.Hosting"],
            },
            // No root csproj — restore (and the overwrite guard) target the solution, which pulls all three.
            RestoreTarget = $"{name}.slnx",
        };
    }

    // The shell + welcome page are exactly the server/wasm ones, only re-homed into the .Client namespace so
    // the Server's cross-project reference and the client's own types line up. Reuse keeps the three in sync.
    private static string WasmHostedClientAppShell(Styling styling) =>
        AppShellCs(styling).Replace($"namespace {NameToken}.Features.Shared;", $"namespace {NameToken}.Client.Features.Shared;", StringComparison.Ordinal);

    private static string WasmHostedClientHomePage(Styling styling) =>
        HomePageCs(styling).Replace($"namespace {NameToken}.Features.Home;", $"namespace {NameToken}.Client.Features.Home;", StringComparison.Ordinal);

    private static string WasmHostedClientProgram(bool auth, bool pwa, bool cqrs, IReadOnlyList<string> cultures)
    {
        var sb = new StringBuilder();
        sb.Append($"using {NameToken}.Client.Features.Shared;\n"); // App lives in the client's Features/Shared bucket.
        sb.Append("using Microsoft.Extensions.DependencyInjection;\n");
        sb.Append("using Rask.Wasm;\n");
        if (cqrs)
        {
            sb.Append("using Rask.Cqrs.Client;\n");
            sb.Append("using Rask.Query;\n");
        }

        if (auth)
        {
            sb.Append($"using {NameToken}.Client.Features.Auth;\n");
            sb.Append("using Rask.Core.Authentication;\n");
        }

        if (pwa)
        {
            sb.Append("using Rask.Core.Browser;\n");
        }

        sb.Append("""

            // PathBase is auto-detected from <base href>. Publish with /p:RaskPathBase=/myapp
            // for sub-path deploys, or override via CreateDefault(o => o.PathBase = "/myapp").
            var host = WasmHostBuilder.CreateDefault();

            // Same-origin HttpClient so the client can call its own host's API endpoints.
            host.Services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri(WasmHostBuilder.BaseAddress) });

            """.TrimStart('\n'));

        sb.Append(WasmUseCulture(cultures));

        if (cqrs)
        {
            sb.Append("""

                // Remote CQRS: this project becomes a pure client. Dispatching a message defined in Shared
                // sends it to the server and returns the handler's answer — the same IDispatcher call an
                // in-process app makes, with no HttpClient at the call site and no endpoint to write. The
                // request is same-origin, so the auth cookie rides it. A message this client owns end to
                // end needs [LocalOnly], or it travels too. See docs/cqrs.md.
                host.Services.AddRaskCqrsClient();

                // Server state over that dispatcher: dedup, staleness, background refetch, invalidation.
                // Worth more here than anywhere — every dispatch is a network round trip, so a component
                // that refetches on each render is paying for it over the wire. See docs/query.md.
                host.Services.AddRaskQuery();

                """.TrimStart('\n'));
        }

        if (pwa)
        {
            sb.Append("""

                // Installable PWA: the framework injects <link rel="manifest"> + <meta name="theme-color"> at boot.
                host.UsePwa(new WebAppManifest
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

        if (auth)
        {
            sb.Append("""

                // Hydrates the user from the host's /api/me (HttpOnly cookie); WasmLoginService drives sign-in/out.
                host.Services.AddSingleton<ApiUserProvider>();
                host.Services.AddSingleton<IUserProvider>(sp => sp.GetRequiredService<ApiUserProvider>());
                host.Services.AddSingleton<WasmLoginService>();

                """.TrimStart('\n'));
        }

        sb.Append("\nawait host.RunAsync<App>();\n");
        return sb.ToString();
    }

    private static string WasmHostedServerProgram(ServerBatteries batteries)
    {
        var auth = batteries.Auth;
        var sb = new StringBuilder();
        if (auth)
        {
            sb.Append($"using {NameToken}.Server.Features.Auth;\n"); // ICredentialStore / DemoCredentialStore
            sb.Append($"using {NameToken}.Shared;\n");
            sb.Append("using System.Security.Claims;\n");
            sb.Append("using Microsoft.AspNetCore.Authentication;\n");
            sb.Append("using Microsoft.AspNetCore.Authentication.Cookies;\n");
        }

        if (batteries.Data)
        {
            sb.Append($"using {NameToken}.Server.Features.Shared;\n"); // AppDbContext
        }

        if (batteries.Cqrs)
        {
            sb.Append("using Rask.Cqrs.Server;\n");
        }

        sb.Append("using Microsoft.AspNetCore.HttpOverrides;\n");
        sb.Append("using Rask.Wasm.Hosting;\n");
        if (batteries.Ops)
        {
            // Rask.Server for AddRaskServer/UseRaskServer; Rask.Dashboard for the shell and the policy name.
            sb.Append("using Rask.Server;\n");
        }

        sb.Append(DatabaseAndBatteryUsings(batteries));

        sb.Append("""

            var builder = WebApplication.CreateBuilder(args);

            // Opt into brotli + gzip response compression for the published wwwroot (.wasm / .js / .json).
            // Named AddRaskWasmHost, not AddRask: with the operator dashboard on, this project references
            // Rask.Server too, and both packages define an AddRask(this IServiceCollection). A bare call
            // is NOT reported as ambiguous — this one takes no optional parameters, so it wins the
            // "fewer defaulted arguments" tie-break silently — and the app would start with no live
            // runtime and fail on the first request. Each call names the host it means.
            builder.Services.AddRaskWasmHost();

            // A liveness/readiness endpoint (mapped below). `rask deploy` probes it to gate the blue-green
            // swap; also useful for any load balancer or orchestrator. Add real checks later, e.g.
            // .AddDbContextCheck<AppDb>(). (No AddRaskLiveSessions here: this host serves a WASM bundle and
            // an API — the live-session pool it reports on belongs to the server-rendered template.)
            builder.Services.AddHealthChecks();

            // Behind a reverse proxy (`rask deploy` runs Caddy in front), the app otherwise sees the proxy's
            // address and a plain-HTTP request — so Request.Scheme is "http", UseHsts never emits, and
            // RemoteIpAddress is the proxy. The proxy's container IP is assigned by Docker, so it can't be
            // named in KnownProxies; clearing the lists is safe here because the container publishes no host
            // port. Delete this block if you expose the app directly (a client could then forge its IP).
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });

            """.TrimStart('\n'));

        // With --data the host owns a SQLite file, so shutdown has to leave room for the WAL checkpoint
        // and the Litestream flush — the same budget the server template takes. Without it there is no
        // file to close and nothing for the budget to cover.

        if (batteries.Ops)
        {
            Block(sb, """
                // The live runtime, for the operator dashboard only. This host serves a WASM bundle — the
                // application's own UI runs in the browser and needs nothing from here — but the dashboard
                // is server-rendered, so its pages need a session store and the WebSocket its panels
                // update over. Named AddRaskServer rather than AddRask: see AddRaskWasmHost above.
                builder.Services.AddRaskServer();
                """);
        }

        // The database and every DB-backed battery, byte-for-byte what the server template emits.
        AppendDatabaseAndBatteries(sb, batteries);

        if (auth)
        {
            sb.Append("""

                builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(o =>
                    {
                        o.Cookie.Name = "rask.auth";
                        // Secure-by-default: HTTPS-only and SameSite=Lax (so the cookie doesn't ride cross-site
                        // POSTs — the primary CSRF mitigation for the /api/login POST below). The dev launch
                        // profile runs on HTTPS; relax SecurePolicy only if you must serve over plain HTTP.
                        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                        o.Cookie.SameSite = SameSiteMode.Lax;
                    });
                builder.Services.AddSingleton<ICredentialStore, DemoCredentialStore>();

                """.TrimStart('\n'));
        }

        if (batteries.Cqrs)
        {
            // Two registrations, one flag. With --auth there is a real user, so the secure default stands:
            // every message needs an authenticated caller unless its handler says [AllowAnonymous]. Without
            // --auth there is nobody to authenticate, and leaving the default on would 401 every message the
            // app has — a template that cannot run its own sample. So it is turned off explicitly, where the
            // comment can say what turning it back on requires.
            Block(sb, batteries.Auth
                ? """
                    // Remote CQRS: AddRaskCqrsServer also calls AddRaskCqrs, so this registers the mediator AND
                    // the endpoint options in one line. It fails closed — a message is reachable only by an
                    // authenticated caller, unless its handler carries [AllowAnonymous]; [Authorize] on a
                    // handler supplies the policy and the roles. See docs/cqrs.md.
                    builder.Services.AddRaskCqrsServer();
                    """
                : """
                    // Remote CQRS: AddRaskCqrsServer also calls AddRaskCqrs, so this registers the mediator AND
                    // the endpoint options in one line.
                    //
                    // RequireAuthenticatedUser is OFF because this template has no authentication to require —
                    // left on, every message would answer 401 and nothing would work. Scaffold with --auth (or
                    // add your own cookie/JWT scheme) and DELETE this argument: the default is on for a reason,
                    // and a message reachable by anyone is a decision worth making per app.
                    builder.Services.AddRaskCqrsServer(o => o.RequireAuthenticatedUser = false);
                    """);
        }

        sb.Append("""

            var app = builder.Build();

            // FIRST: rewrite Request.Scheme/RemoteIpAddress from the proxy's headers, so everything below
            // sees the request the visitor actually made.
            app.UseForwardedHeaders();

            // Health endpoint next — as terminal middleware it short-circuits before UseHttpsRedirection,
            // so /health answers 200 over plain HTTP. `rask deploy` probes it internally on http://…:8080
            // (no X-Forwarded-Proto), where a redirected endpoint would 307 to a port nothing listens on.
            app.UseHealthChecks("/health");

            // Transport security (applies whether or not auth is enabled): redirect HTTP→HTTPS, and in
            // non-Development emit HSTS so browsers refuse plain-HTTP for the configured max-age.
            if (!app.Environment.IsDevelopment())
            {
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            """.TrimStart('\n'));

        if (auth)
        {
            sb.Append("""

                // Populates HttpContext.User from the cookie so /api/me reflects the signed-in user.
                app.UseAuthentication();
                // Present so a [Authorize]/RequireAuthorization() you add to an endpoint is actually enforced.
                app.UseAuthorization();

                // Auth API consumed by the WASM client (same origin, so the HttpOnly cookie rides every request).
                app.MapPost("/api/login", async (HttpContext ctx, LoginRequest dto, ICredentialStore creds) =>
                {
                    var claims = creds.Validate(dto.Username, dto.Password);
                    if (claims is null) return Results.Unauthorized();
                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    await ctx.SignInAsync(new ClaimsPrincipal(identity));
                    return Results.Ok();
                });

                app.MapGet("/api/me", (HttpContext ctx) =>
                    ctx.User.Identity?.IsAuthenticated == true
                        ? Results.Ok(new MeDto(ctx.User.Identity!.Name!, ctx.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray()))
                        : Results.NoContent());

                app.MapPost("/auth/logout", async (HttpContext ctx) => { await ctx.SignOutAsync(); return Results.Ok(); });

                """.TrimStart('\n'));
        }

        if (batteries.Cqrs)
        {
            Block(sb, """
                // The endpoint pair every remotely dispatched message arrives on: GET for queries, POST for
                // commands, both under /_rask/cqrs/request/{name}. Two routes however many messages the app
                // grows, and the verb carries what IQuery and ICommand already declare — so a command is 405
                // on GET and cannot be triggered by a URL, a prefetch or a link scanner.
                //
                // Literal route segments, so they outrank both the dashboard's /_rask catch-all and the SPA
                // fallback below whichever order they are registered in. Returns the endpoint group, so
                // .RequireRateLimiting(...) or a CORS policy is a one-line addition here.
                app.MapRaskCqrs();
                """);
        }

        if (batteries.Ops)
        {
            Block(sb, """
                // The operator dashboard, server-rendered under its own prefix. This is the one part of a
                // wasm-hosted app that does NOT run in the browser: it reads the batteries' tables straight
                // out of the database, which only this host can reach.
                //
                // Scoped to "/_rask/{**path}" so it claims the dashboard's routes and nothing else — the
                // SPA fallback below is a MapFallback, the lowest precedence there is, so every other route
                // still reaches the client. The framework's own /_rask endpoints (the scoped assets, the
                // upload and download paths) are literal routes and outrank this catch-all, so they keep
                // working; don't give a dashboard page a route that collides with one of them.
                app.UseRaskServer<RaskDashboardShell>("/_rask/{**path}");
                """);
        }

        sb.Append("""

            // Serve the baked WASM bundle: UseDefaultFiles + UseStaticFiles (pre-compressed .br/.gz siblings)
            // + a SPA fallback to index.html for client-side routes. Non-generic on purpose — the host serves
            // static files and never runs the components, so it needs no reference to the client's App type.
            app.UseRaskWasmHost();

            app.Run();

            """.TrimStart('\n'));

        return sb.ToString();
    }

    private static string WasmHostedNextSteps(string name, bool docker)
    {
        var steps = new StringBuilder();
        steps.Append("Created ").Append(name).Append(" (Rask WASM + ASP.NET host).\n\nNext steps:\n");
        steps.Append("  cd ").Append(name).Append('\n');
        steps.Append("  rask dev            # run the host with hot reload (finds the .Server project)\n");
        if (docker)
        {
            steps.Append("  docker build -t ").Append(name.ToLowerInvariant()).Append(" .   # then: docker run -p 8080:8080 …\n");
        }

        return steps.ToString();
    }

    // ---- wasm-hosted template files ----

    // Rask.Cqrs lands HERE rather than in either half, and that placement is the design: a message is a
    // contract, so both sides must compile the same record. The Client adds Rask.Cqrs.Client and the Server
    // adds Rask.Cqrs.Server on top — neither references the other's transport, so the browser bundle cannot
    // compile the endpoint code and the host never carries the browser transport.
    private static string SharedCsproj(bool cqrs, string version)
    {
        var cqrsRef = cqrs
            ? $"\n\n  <ItemGroup>\n    <PackageReference Include=\"Rask.Cqrs\" Version=\"{version}\"/>\n  </ItemGroup>"
            : "";

        return $"""
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>{cqrsRef}

        </Project>

        """;
    }

    private const string WasmHostedSharedContracts =
        """
        namespace Company.RaskServer.Shared;

        // Types shared between the Client and the Server go here — e.g. request/response DTOs for your API
        // endpoints, so the browser client and the ASP.NET host bind to one definition instead of duplicating it.
        """;

    private const string WasmHostedSharedContractsAuth =
        """
        using System.Text.Json.Serialization;

        namespace Company.RaskServer.Shared;

        // Contracts shared between the Client and the Server. The auth DTOs live here so the browser client
        // and the ASP.NET host bind to one definition instead of duplicating it. The JsonPropertyName casing
        // is honoured by both the client's source-generated serializer and the host's minimal-API binding.
        public sealed record LoginRequest(
            [property: JsonPropertyName("username")] string Username,
            [property: JsonPropertyName("password")] string Password);

        public sealed record MeDto(
            [property: JsonPropertyName("name")] string Name,
            [property: JsonPropertyName("roles")] string[] Roles);

        """;

    // Shares the WebAssembly SDK property block with the standalone `wasm` template (WasmSdkPropertyGroup in
    // ProjectGenerator.Wasm.cs) so the two can't drift. The hosted client differs only in referencing the
    // Shared project (and never the --auth JSInterop/Authorization refs — hosted auth is cookie-based).
    private static string WasmHostedClientCsproj(Styling styling, string version, bool cqrs, bool localization)
    {
        var bootstrapRef = styling == Styling.Bootstrap
            ? $"\n    <PackageReference Include=\"Rask.Bootstrap\" Version=\"{version}\"/>"
            : "";

        // Build-only, so it adds nothing to what the browser downloads: the package is a props/targets
        // pair plus the MSBuild task that resolves the Tailwind compiler.
        var tailwindRef = styling == Styling.Tailwind
            ? $"\n    <PackageReference Include=\"Rask.Tailwind\" Version=\"{version}\"/>"
            : "";

        // The client half only. It has no idea an endpoint exists — it turns a dispatch into a request.
        // Rask.Query rides along with it: a cache over the dispatcher is not a separate decision from
        // having a dispatcher, and over a network transport it is the difference between one request and
        // one per render.
        var cqrsRef = cqrs
            ? $"\n    <PackageReference Include=\"Rask.Cqrs.Client\" Version=\"{version}\"/>"
              + $"\n    <PackageReference Include=\"Rask.Query\" Version=\"{version}\"/>"
            : "";

        return $"""
        <Project Sdk="Microsoft.NET.Sdk.WebAssembly">

        {WasmSdkPropertyGroup(localization)}

          <ItemGroup>
            <PackageReference Include="Rask.Wasm" Version="{version}"/>{bootstrapRef}{tailwindRef}{cqrsRef}
            <ProjectReference Include="..\Company.RaskServer.Shared\Company.RaskServer.Shared.csproj"/>
          </ItemGroup>

        </Project>

        """;
    }

    /// <summary>
    /// What the <c>.Server</c> host references. The battery packages are the server template's, unchanged —
    /// the same <c>AddRaskX&lt;AppDbContext&gt;()</c> calls need the same references — so they come from
    /// <see cref="ServerPackages"/> rather than from a second list that would fall behind it.
    /// </summary>
    private static List<string> WasmHostedServerPackages(ServerBatteries batteries)
    {
        // Ops is cleared here and handled below so the two packages it implies are added together and in
        // one place; Bootstrap belongs to the .Client, which is what renders the application's UI.
        var packages = ServerPackages(batteries with { Styling = Styling.Plain, Ops = false });
        packages.Remove("Rask.Server");
        packages.Insert(0, "Rask.Wasm.Hosting");

        if (batteries.Cqrs)
        {
            // Rask.Cqrs.Server REPLACES the bare mediator package here: it depends on it and adds the
            // endpoint pair, and AddRaskCqrsServer calls AddRaskCqrs for you. On this template --cqrs means
            // the client dispatches to this host, which the mediator alone cannot do.
            packages.Remove("Rask.Cqrs");
            packages.Add("Rask.Cqrs.Server");
        }

        if (batteries.Ops)
        {
            // Rask.Server rides along ONLY for the dashboard. It is what supplies UseRaskServer<TApp>, the
            // live session runtime, and the WebSocket the dashboard's polling panels update over — a
            // wasm-hosted app without --ops runs no components on the host and has no use for any of it.
            packages.Add("Rask.Server");
            packages.Add("Rask.Dashboard");
        }

        return packages;
    }

    /// <summary>The app's <c>DbContext</c>, re-homed into the <c>.Server</c> project's namespace.</summary>
    private static string WasmHostedServerDbContext(ServerBatteries batteries) =>
        AppDbContextCs(batteries).Replace(
            $"namespace {NameToken}.Features.Shared;",
            $"namespace {NameToken}.Server.Features.Shared;",
            StringComparison.Ordinal);

    private static string WasmHostedServerCsproj(ServerBatteries batteries, string version)
    {
        var refs = new StringBuilder();
        foreach (var package in WasmHostedServerPackages(batteries).Skip(1))
        {
            refs.Append($"\n    <PackageReference Include=\"{package}\" Version=\"{version}\"/>");
        }

        // See ServerCsproj: Litestream's build props fetch a binary from GitHub releases unless told not
        // to, which breaks an offline build and errors outright on a RID with no published asset.
        var litestreamProperty = batteries.Data
            ? "\n    <!-- The litestream binary ships in the Docker image, not fetched at build time. -->"
              + "\n    <RaskLitestreamDownload>false</RaskLitestreamDownload>"
            : "";

        return $"""
        <Project Sdk="Microsoft.NET.Sdk.Web">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>{litestreamProperty}
            <!--
              This host serves the published WASM bundle from the publish directory at runtime
              (app.UseRask()), not via the SDK's static-web-assets manifest, so it owns no static web
              assets. Disabling the SWA pipeline skips the cross-project static-web-asset resolution that
              otherwise races on a clean parallel build, when the host tries to resolve the WASM project's
              fingerprinted dotnet.native.* assets before that project has emitted them (MSB4018). Build
              ordering is unaffected. Remove this if you add static web assets (e.g. an RCL) to the host.
            -->
            <StaticWebAssetsEnabled>false</StaticWebAssetsEnabled>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Rask.Wasm.Hosting" Version="{version}"/>{refs}
            <ProjectReference Include="..\Company.RaskServer.Shared\Company.RaskServer.Shared.csproj"/>
            <!--
              Cross-TFM reference: a net10.0 host pointing at a net10.0-browser WASM client.
              ReferenceOutputAssembly=false + SkipGetTargetFrameworkProperties=true are both
              required (NU1201 otherwise). The Rask.Wasm.Hosting MSBuild targets that ship in
              the NuGet auto-discover this ProjectReference, publish the WASM wwwroot, and
              bake the published directory into the host assembly so `app.UseRask()` resolves it.
            -->
            <ProjectReference Include="..\Company.RaskServer.Client\Company.RaskServer.Client.csproj"
                              ReferenceOutputAssembly="false"
                              SkipGetTargetFrameworkProperties="true"/>
          </ItemGroup>

        </Project>

        """;
    }

    private const string WasmHostedServerLaunchSettings =
        """
        {
          "profiles": {
            "Company.RaskServer.Server": {
              "commandName": "Project",
              "launchBrowser": true,
              "applicationUrl": "https://localhost:5001;http://localhost:5000",
              "environmentVariables": {
                "ASPNETCORE_ENVIRONMENT": "Development"
              }
            }
          }
        }

        """;

    private const string WasmHostedServerCredentialStore =
        """
        using System.Security.Claims;

        namespace Company.RaskServer.Server.Features.Auth;

        // Demo credential store — replace with your real user store (ASP.NET Identity, a database, etc.).
        public interface ICredentialStore
        {
            IReadOnlyList<Claim>? Validate(string username, string password);
        }

        public sealed class DemoCredentialStore : ICredentialStore
        {
            public IReadOnlyList<Claim>? Validate(string username, string password) =>
                (username, password) switch
                {
                    ("alice", "password") => [new Claim(ClaimTypes.Name, "alice"), new Claim(ClaimTypes.Role, "user")],
                    ("root", "password") => [new Claim(ClaimTypes.Name, "root"), new Claim(ClaimTypes.Role, "admin")],
                    _ => null
                };
        }

        """;

    private const string WasmHostedClientAuth =
        """
        using System.Net.Http.Json;
        using System.Security.Claims;
        using System.Text.Json.Serialization;
        using Company.RaskServer.Shared;
        using Rask.Core.Authentication;
        using Rask.Core.Routing;

        namespace Company.RaskServer.Client.Features.Auth;

        public sealed class LoginModel
        {
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
        }

        // Source-generated JSON keeps the WASM publish trim-clean (zero IL warnings). The DTOs it serializes
        // live in the Shared project — the host binds the very same records.
        [JsonSerializable(typeof(LoginRequest))]
        [JsonSerializable(typeof(MeDto))]
        public partial class AuthJson : JsonSerializerContext;

        // Hydrates the principal from the host's /api/me (the HttpOnly cookie rides the same-origin request).
        public sealed class ApiUserProvider(HttpClient http) : IUserProvider
        {
            private ClaimsPrincipal _current = new(new ClaimsIdentity());
            public ClaimsPrincipal Current => _current;
            public bool IsLoading { get; private set; }
            public event Action? Changed;

            public Task EnsureLoadedAsync()
            {
                IsLoading = true; // bridge the anonymous→authed flash (LoadAsync's finally clears it)
                return LoadAsync();
            }

            public async Task RefreshAsync()
            {
                IsLoading = true;
                Changed?.Invoke();
                await LoadAsync();
            }

            private async Task LoadAsync()
            {
                try
                {
                    // GetAsync (not GetFromJsonAsync): an anonymous /api/me returns 204 No Content, and
                    // GetFromJsonAsync would throw a JsonException deserializing the empty body. Treat
                    // anything but a 200-with-body as anonymous.
                    using var resp = await http.GetAsync("api/me");
                    var me = resp.StatusCode == System.Net.HttpStatusCode.OK
                        ? await resp.Content.ReadFromJsonAsync(AuthJson.Default.MeDto)
                        : null;
                    _current = me is { Name: { } name }
                        ? new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.Name, name), .. me.Roles.Select(r => new Claim(ClaimTypes.Role, r))], "api"))
                        : new ClaimsPrincipal(new ClaimsIdentity());
                }
                catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
                {
                    _current = new ClaimsPrincipal(new ClaimsIdentity());
                }
                finally
                {
                    IsLoading = false;
                    Changed?.Invoke();
                }
            }
        }

        public sealed class WasmLoginService(HttpClient http, IUserProvider users, Navigator nav)
        {
            public async Task<bool> LoginAsync(string username, string password, string? returnUrl)
            {
                var resp = await http.PostAsJsonAsync("api/login", new LoginRequest(username, password), AuthJson.Default.LoginRequest);
                if (!resp.IsSuccessStatusCode) return false;
                await users.RefreshAsync();
                // Open-redirect guard: an attacker-supplied returnUrl must never navigate off-origin.
                nav.NavigateTo(LocalUrl.Sanitize(returnUrl ?? "/members"));
                return true;
            }

            public async Task LogoutAsync()
            {
                await http.PostAsync("auth/logout", null);
                // Navigate first (still in the handler scope), then clear the principal.
                nav.NavigateTo(Routes.LoginPage());
                await users.RefreshAsync();
            }
        }

        """;

    // The messages. Nothing here says "remote": these are the same records an in-process app writes, and
    // where the project sits decides where they run. That is the whole DX claim — a feature moves between
    // in-process and client/server without its call sites changing.
    private const string WasmHostedSharedMessages =
        """
        using Rask.Cqrs;

        namespace Company.RaskServer.Shared;

        // A query: safe and idempotent, so it travels as a GET and can be cached.
        public sealed record GetGreeting(string Name) : IQuery<Greeting>;

        public sealed record Greeting(string Message, DateTimeOffset ServerTime);

        // A command: it mutates, so it travels as a POST and can never be triggered by a URL, a prefetch or
        // a link scanner. The transport enforces that from the type alone — GET answers 405.
        public sealed record RecordVisit(string Name) : ICommand<int>;

        """;

    // Handlers live in the SERVER project and are never compiled into the browser bundle. The client
    // references the message, not the handler — which is what keeps a connection string, a table name or a
    // pricing rule out of a download anybody can read.
    private const string WasmHostedServerHandlers =
        """
        using Company.RaskServer.Shared;
        using Rask.Cqrs;

        namespace Company.RaskServer.Server.Features.Hello;

        // An ordinary handler. It has no idea the call arrived over HTTP: the same class serves an
        // in-process dispatch unchanged, which is why a feature can start local and become remote later.
        public sealed class GetGreetingHandler : IQueryHandler<GetGreeting, Greeting>
        {
            public Task<Greeting> HandleAsync(GetGreeting query, CancellationToken cancellationToken) =>
                Task.FromResult(new Greeting($"Hello, {query.Name}, from the server.", DateTimeOffset.UtcNow));
        }

        // Counts in memory so the template needs no database. Swap the field for a DbSet when you add one —
        // inject the context through the constructor exactly as you would in-process.
        public sealed class RecordVisitHandler : ICommandHandler<RecordVisit, int>
        {
            private static int _visits;

            public Task<int> HandleAsync(RecordVisit command, CancellationToken cancellationToken) =>
                Task.FromResult(Interlocked.Increment(ref _visits));
        }

        """;

    // The page that makes the claim concrete: no HttpClient here, no endpoint written for it, no serializer
    // registered. IDispatcher is the same interface an in-process app injects — AddRaskCqrsClient is what
    // decides the message leaves the browser.
    private const string WasmHostedClientHelloPage =
        """
        using Company.RaskServer.Shared;
        using Rask.Core.Components;
        using Rask.Core.Routing;
        using Rask.Cqrs;

        namespace Company.RaskServer.Client.Features.Hello;

        [Route("hello")]
        public sealed partial class HelloPage(IDispatcher dispatcher) : Component
        {
            private readonly HelloModel _model = new();
            private Greeting? _greeting;
            private int? _visits;
            private string? _error;

            protected override Component? Render() =>
                Div.Style("max-width:32rem;margin:3rem auto;font-family:system-ui")[
                    H1["Remote CQRS"],
                    P["The button below dispatches a query and a command. Both handlers run on the server."],
                    _error is null ? null : Div.Id("hello-error").Style("color:#b00020")[_error],
                    Form.Model(_model).OnValidSubmitAsync(AskAsync)[
                        Div[Label.For("name")["Your name"], Input.Bind(() => _model.Name).Id("name")],
                        Button.Type("submit").Id("hello-submit")["Ask the server"]
                    ],
                    _greeting is null ? null : P.Id("hello-greeting")[_greeting.Message],
                    _visits is null ? null : P.Id("hello-visits")[$"Visits recorded: {_visits}"]
                ];

            private async Task AskAsync(HelloModel m)
            {
                try
                {
                    // A query. It is safe and idempotent, so it travels as a GET — cacheable, and incapable
                    // of changing anything. The result type comes from the message, so this is Greeting
                    // without a cast and without a serializer to register.
                    _greeting = await dispatcher.DispatchAsync(new GetGreeting(m.Name));

                    // A command. It mutates, so it travels as a POST — and the transport will not send it any
                    // other way, which is what stops a URL, a prefetch or a link scanner from triggering it.
                    _visits = await dispatcher.DispatchAsync(new RecordVisit(m.Name));
                    _error = null;
                }
                catch (RemoteDispatchException ex)
                {
                    // The one thing remote dispatch adds to the in-process call: it can fail to arrive. A null
                    // StatusCode means the request never reached the server at all.
                    _error = ex.StatusCode is null
                        ? "Could not reach the server."
                        : $"The server refused the request ({ex.StatusCode}).";
                }
            }
        }

        public sealed class HelloModel
        {
            public string Name { get; set; } = "world";
        }

        """;

    private const string WasmHostedClientLoginPage =
        """
        using Microsoft.AspNetCore.Authorization;
        using Rask.Core.Components;
        using Rask.Core.Routing;

        namespace Company.RaskServer.Client.Features.Auth;

        [AllowAnonymous]
        [Route("login")]
        public sealed partial class LoginPage(WasmLoginService login) : Component
        {
            private readonly LoginModel _model = new();
            private string? _error;

            [QueryParam] public string? ReturnUrl { get; set; }

            protected override Component? Render() =>
                Div.Style("max-width:22rem;margin:3rem auto;font-family:system-ui")[
                    H1["Sign in"],
                    _error is null ? null : Div.Style("color:#b00020")[_error],
                    Form.Model(_model).OnValidSubmitAsync(SubmitAsync)[
                        Div[Label.For("username")["Username"], Input.Bind(() => _model.Username).Id("username")],
                        Div[Label.For("password")["Password"], Input.Bind(() => _model.Password).Id("password").Type(InputType.Password)],
                        Button.Type("submit").Id("login-submit")["Sign in"]
                    ],
                    P["Try alice / password (user) or root / password (admin)."]
                ];

            private async Task SubmitAsync(LoginModel m)
            {
                if (!await login.LoginAsync(m.Username, m.Password, ReturnUrl))
                {
                    _error = "Invalid username or password.";
                }
            }
        }

        """;

    private const string WasmHostedClientMembersPage =
        """
        using Microsoft.AspNetCore.Authorization;
        using Rask.Core.Authentication;
        using Rask.Core.Components;
        using Rask.Core.Routing;

        namespace Company.RaskServer.Client.Features.Auth;

        // On WASM there's no server route guard — the Authorize component gates the content off the principal
        // (hydrated from /api/me). The signed-in view is a child component so it reads the fresh principal when
        // the gate opens after sign-in.
        [AllowAnonymous]
        [Route("members")]
        public sealed partial class MembersPage : Component
        {
            protected override Component? Render() =>
                Div.Style("max-width:32rem;margin:3rem auto;font-family:system-ui")[
                    Authorize
                        .NotAuthorized(P["Please ", NavLink.Href(Routes.LoginPage())["sign in"], "."])[MemberContent]
                ];
        }

        public sealed partial class MemberContent(WasmLoginService login, IUserProvider userProvider) : Component
        {
            protected override Component? Render() =>
                [
                    H1[$"Welcome, {userProvider.Current.Identity?.Name}"],
                    Authorize.Roles(["admin"])[
                        Div.Style("color:#7a5c00")["🔑 You have admin access."]],
                    Button.Id("logout").OnClickAsync(login.LogoutAsync)["Sign out"]
                ];
        }

        """;

    private const string WasmHostedDockerfile =
        """
        # Multi-stage build for the hosted solution: build the WASM client and its ASP.NET Server, then run
        # the Server (which serves the baked client bundle) on the aspnet runtime.
        FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
        # The browser-wasm client the Server bakes in needs the wasm-tools workload to publish.
        RUN dotnet workload install wasm-tools
        WORKDIR /src

        # Copy the whole solution — the Server's ProjectReference to the WASM client makes a
        # csproj-only restore layer brittle, so restore happens inside publish for correctness.
        COPY . .
        RUN dotnet publish "Company.RaskServer.Server/Company.RaskServer.Server.csproj" -c Release -o /app

        FROM mcr.microsoft.com/dotnet/aspnet:10.0
        WORKDIR /app
        COPY --from=build /app .

        # A writable data directory for the SQLite database, owned by the image's non-root runtime user
        # ($APP_UID) — the same preparation the server template's Dockerfile does, and for the same reason:
        # `rask deploy` mounts a named volume at /data and points the app at /data/app.db unconditionally.
        # Without this the mount lands root-owned and a non-root app can't create the database in it.
        USER root
        RUN mkdir -p /data && chown $APP_UID:$APP_UID /data
        USER $APP_UID

        EXPOSE 8080
        # The Server calls UseHttpsRedirection(); inside the container no HTTPS port is configured,
        # so it no-ops. Terminate TLS at your reverse proxy / ingress and forward plain HTTP to 8080.
        ENTRYPOINT ["dotnet", "Company.RaskServer.Server.dll"]

        """;
}
