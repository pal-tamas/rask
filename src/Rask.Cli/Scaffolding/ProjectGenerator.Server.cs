using System.Text;

namespace Rask.Cli.Scaffolding;

// The server template: an ASP.NET live-server Rask app.
internal static partial class ProjectGenerator
{
    /// <summary>Generates the <c>server</c> template into <paramref name="targetDirectory"/>.</summary>
    public static ScaffoldResult GenerateServer(string targetDirectory, string name, bool auth, bool pwa, bool cqrs, bool data, bool docker, string version)
    {
        // --data pre-wires a database: an AppDbContext + AddRaskData + a UseRaskSqlite DbContext factory. The
        // CQRS mediator is part of that story (every `rask generate feature` handler dispatches through it), so
        // --data implies --cqrs — one flag gives a fresh app the whole "feature → migrate" loop.
        cqrs = cqrs || data;

        var files = new List<(string Path, string Content)>
        {
            ($"{NameToken}.csproj", ServerCsproj(cqrs, data, version)),
            ("Program.cs", ServerProgram(auth, pwa, cqrs, data)),
            ("App.cs", AppCs),
            ("Properties/launchSettings.json", LaunchSettings),
        };

        if (auth)
        {
            files.Add(("Auth/CredentialStore.cs", AuthCredentialStore));
            files.Add(("Auth/LoginPage.cs", AuthLoginPage));
            files.Add(("Auth/MembersPage.cs", AuthMembersPage));
        }

        if (data)
        {
            files.Add(("Data/AppDbContext.cs", AppDbContextCs));
        }

        if (pwa)
        {
            files.Add(("wwwroot/icon.svg", IconSvg));
            files.Add(("wwwroot/offline.html", OfflineHtml));
        }

        if (docker)
        {
            files.Add(("Dockerfile", Dockerfile));
            files.Add((".dockerignore", DockerIgnore));
        }

        var scaffoldFiles = Materialize(targetDirectory, name, files);

        var packages = new List<string> { "Rask.Server", "Rask.Bootstrap" };
        if (cqrs)
        {
            packages.Add("Rask.Cqrs");
        }

        if (data)
        {
            packages.Add("Rask.Data");
            packages.Add("Rask.SQLite.EntityFrameworkCore");
        }

        return new ScaffoldResult(scaffoldFiles, ServerNextSteps(name, docker, data)) { Packages = packages };
    }

    private static string ServerCsproj(bool cqrs, bool data, string version)
    {
        var cqrsRef = cqrs ? $"\n    <PackageReference Include=\"Rask.Cqrs\" Version=\"{version}\"/>" : "";
        // Rask.SQLite.EntityFrameworkCore brings UseRaskSqlite (WAL/busy_timeout pragmas) and pulls
        // Microsoft.EntityFrameworkCore.Sqlite transitively; `rask db` adds EF's Design package on the
        // first migration, so the base app builds and runs with no design-time dependency.
        var dataRef = data
            ? $"\n    <PackageReference Include=\"Rask.Data\" Version=\"{version}\"/>"
              + $"\n    <PackageReference Include=\"Rask.SQLite.EntityFrameworkCore\" Version=\"{version}\"/>"
            : "";
        return $"""
        <Project Sdk="Microsoft.NET.Sdk.Web">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Rask.Server" Version="{version}"/>
            <PackageReference Include="Rask.Bootstrap" Version="{version}"/>{cqrsRef}{dataRef}
          </ItemGroup>

        </Project>

        """;
    }

    private static string ServerProgram(bool auth, bool pwa, bool cqrs, bool data)
    {
        var sb = new StringBuilder();
        sb.Append("using Company.RaskServer;\nusing Rask.Server;\n");
        if (auth)
        {
            sb.Append("using Microsoft.AspNetCore.Authentication.Cookies;\n");
        }

        if (pwa)
        {
            sb.Append("using Rask.Core.Browser;\n");
        }

        if (cqrs)
        {
            sb.Append("using Rask.Cqrs;\n");
        }

        if (data)
        {
            sb.Append("using Microsoft.EntityFrameworkCore;\n");
            sb.Append("using Microsoft.EntityFrameworkCore.Diagnostics;\n");
            sb.Append("using Rask.Data;\n");
            sb.Append("using Rask.SQLite;\n");
        }

        sb.Append("\nvar builder = WebApplication.CreateBuilder(args);\n\n");
        sb.Append("builder.Services.AddRask();\n");
        // A liveness/readiness endpoint (mapped below) — reports the app is up and serving. `rask deploy`
        // probes it to gate the blue-green swap; also useful for any load balancer or orchestrator. Register
        // real dependency checks later, e.g. builder.Services.AddHealthChecks().AddDbContextCheck<AppDb>().
        sb.Append("builder.Services.AddHealthChecks();\n");

        if (cqrs)
        {
            sb.Append("""

                // CQRS mediator: one call registers every IQueryHandler/ICommandHandler/INotificationHandler in
                // this assembly (source-generated, reflection-free — trim/AOT-safe). Inject IDispatcher to send
                // messages; add pipeline behaviors with o.AddOpenBehavior(...). See docs/cqrs.md.
                builder.Services.AddRaskCqrs();

                """.TrimStart('\n'));
        }

        if (data)
        {
            sb.Append("""

                // The app's database, on its own disk — no external server. AddRaskData registers the
                // auditing/soft-delete/concurrency/domain-event interceptors; UseRaskSqlite is a drop-in for
                // UseSqlite that also applies the production pragmas (WAL, busy_timeout, foreign_keys). The
                // connection string defaults to a local app.db but honours a ConnectionStrings:App override —
                // `rask deploy` sets that to a path on a mounted volume so the DB survives redeploys.
                // `rask generate feature X --context AppDbContext` adds a DbSet to AppDbContext;
                // `rask db add <Name>` / `rask db update` create and apply the migration.
                builder.Services.AddRaskData();
                builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
                    .UseRaskSqlite(builder.Configuration.GetConnectionString("App") ?? "Data Source=app.db")
                    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

                """.TrimStart('\n'));
        }

        if (pwa)
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

        if (auth)
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

            // Health endpoint FIRST — as terminal middleware it short-circuits before UseHttpsRedirection,
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

            app.MapStaticAssets();

            """.TrimStart('\n'));

        if (auth)
        {
            sb.Append("// Must precede UseRask so HttpContext.User is populated on the GET and the WS upgrade.\n");
            sb.Append("app.UseAuthentication();\n");
            sb.Append("app.UseAuthorization();\n\n");
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

    private static string ServerNextSteps(string name, bool docker, bool data)
    {
        var steps = new StringBuilder();
        steps.Append("Created ").Append(name).Append(" (Rask server app).\n\nNext steps:\n");
        steps.Append("  cd ").Append(name).Append('\n');
        if (data)
        {
            steps.Append("  rask generate feature Post --fields \"Title:string,Body:string\" --context AppDbContext\n");
            steps.Append("  rask db add Init    # create the first migration\n");
            steps.Append("  rask db update      # apply it to app.db\n");
        }

        steps.Append("  rask dev            # run with hot reload (or: dotnet run)\n");
        if (docker)
        {
            steps.Append("  docker build -t ").Append(name.ToLowerInvariant()).Append(" .   # then: docker run -p 8080:8080 …\n");
        }

        return steps.ToString();
    }

    // ---- --data template files ----

    // An empty database ready for features. `rask generate feature … --context AppDbContext` inserts a DbSet;
    // ApplyRaskConventions + ApplyConfigurationsFromAssembly pick up each feature's generated entity config,
    // so the context needs no per-entity edits beyond its DbSet line.
    private const string AppDbContextCs =
        """
        using Microsoft.EntityFrameworkCore;
        using Rask.Data;

        namespace Company.RaskServer;

        public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
                modelBuilder.ApplyRaskConventions();
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

        namespace Company.RaskServer;

        [Route("login")]
        [AllowAnonymous]
        public sealed class LoginPage(IAuthSignIn auth, ICredentialStore creds) : Component
        {
            private readonly LoginModel _model = new();
            private string? _error;

            [QueryParam] public string? ReturnUrl { get; set; }

            protected override Component? Render() =>
                Div(Class: "welcome-card")[
                    H1()["Sign in"],
                    _error is null ? null : Div(Style: "color:#b00020")[_error],
                    // Async submit uses the generated OnValidSubmitAsync sibling (like Button's OnClickAsync).
                    Form(_model, OnValidSubmitAsync: SubmitAsync)[
                        Div()[Label("username")["Username"], Input(() => _model.Username, Id: "username")],
                        Div()[Label("password")["Password"], Input(() => _model.Password, Id: "password", Type: InputType.Password)],
                        Button("submit")["Sign in"]
                    ],
                    P()["Try alice / password (user) or root / password (admin)."]
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

        namespace Company.RaskServer;

        // [Authorize] blocks anonymous deep-links (full GET → 302 to /login). The Authorize component gates the
        // content and re-renders when the post-sign-in reconnect re-seeds the principal; the signed-in view lives
        // in its own component that injects IUserProvider, so it reads the freshly-authenticated principal — no
        // manual Changed subscription.
        [Route("members")]
        [Authorize]
        public sealed class MembersPage : Component
        {
            protected override Component? Render() =>
                Div(Class: "welcome-card")[
                    Authorize(
                        NotAuthorized: P()["Please ", NavLink(Href: Routes.LoginPage())["sign in"], "."])[MemberContent()]
                ];
        }

        public sealed class MemberContent(IAuthSignIn auth, IUserProvider userProvider) : Component
        {
            protected override Component? Render() =>
                [
                    H1()[$"Welcome, {userProvider.Current.Identity?.Name}"],
                    Authorize(Roles: ["admin"])[
                        Div(Style: "color:#7a5c00")["🔑 You have admin access."]],
                    Button(OnClickAsync: () => auth.SignOutAsync(returnUrl: "/login"))["Sign out"]
                ];
        }

        """;

    private const string Dockerfile =
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

        # A writable data directory for the SQLite database, owned by the image's non-root runtime user
        # ($APP_UID). `rask deploy` mounts a named volume here and points the app at /data/app.db (via
        # ConnectionStrings:App), so the database survives container replacement across redeploys. A fresh
        # named volume inherits this directory's ownership, so the non-root app can create app.db in it.
        USER root
        RUN mkdir -p /data && chown $APP_UID:$APP_UID /data
        USER $APP_UID

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
