using System.Text;

namespace Rask.Cli.Scaffolding;

// The server template: an ASP.NET live-server Rask app.
internal static partial class ProjectGenerator
{
    /// <summary>Generates the <c>server</c> template into <paramref name="targetDirectory"/>.</summary>
    public static ScaffoldResult GenerateServer(string targetDirectory, string name, bool auth, bool pwa, bool cqrs, bool docker, string version)
    {
        var files = new List<(string Path, string Content)>
        {
            ($"{NameToken}.csproj", ServerCsproj(cqrs, version)),
            ("Program.cs", ServerProgram(auth, pwa, cqrs)),
            ("App.cs", ServerApp(cqrs)),
            ("HomePage.cs", HomePage),
            ("HomePage.css", HomePageCss),
            ("Counter.cs", CounterCs),
            ("Weather.cs", WeatherCs),
            ("WeatherForecast.cs", WeatherForecastCs),
            ("LocalWeatherForecastService.cs", LocalWeatherServiceCs),
            ("Properties/launchSettings.json", LaunchSettings),
            ("README.md", ServerReadme),
            ("AGENTS.md", ServerAgents),
        };

        if (auth)
        {
            files.Add(("Auth/CredentialStore.cs", AuthCredentialStore));
            files.Add(("Auth/LoginPage.cs", AuthLoginPage));
            files.Add(("Auth/MembersPage.cs", AuthMembersPage));
        }

        if (cqrs)
        {
            files.Add(("Cqrs/GreetingQuery.cs", CqrsGreetingQuery));
            files.Add(("Cqrs/GreetingPage.cs", CqrsGreetingPage));
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

        return new ScaffoldResult(scaffoldFiles, ServerNextSteps(name, docker)) { Packages = packages };
    }

    private static string ServerCsproj(bool cqrs, string version)
    {
        var cqrsRef = cqrs ? $"\n    <PackageReference Include=\"Rask.Cqrs\" Version=\"{version}\"/>" : "";
        return $"""
        <Project Sdk="Microsoft.NET.Sdk.Web">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Rask.Server" Version="{version}"/>
            <PackageReference Include="Rask.Bootstrap" Version="{version}"/>{cqrsRef}
          </ItemGroup>

        </Project>

        """;
    }

    private static string ServerProgram(bool auth, bool pwa, bool cqrs)
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

        sb.Append("\nvar builder = WebApplication.CreateBuilder(args);\n\n");
        sb.Append("builder.Services.AddRask();\n");
        sb.Append("builder.Services.AddScoped<IWeatherForecastService, LocalWeatherForecastService>();\n");

        if (cqrs)
        {
            sb.Append("""

                // CQRS mediator: one call registers every IQueryHandler/ICommandHandler/INotificationHandler in
                // this assembly (source-generated, reflection-free — trim/AOT-safe). Inject IDispatcher to send
                // messages; add pipeline behaviors with o.AddOpenBehavior(...). See docs/cqrs.md.
                builder.Services.AddRaskCqrs();

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

    private static string ServerApp(bool cqrs)
    {
        var greetingNav = cqrs
            ? """

                            ,
                            " | ",
                            NavLink(GreetingPage())["Greeting"]
                """.TrimEnd() + "\n"
            : "";

        return $$"""
        using static Company.RaskServer.Routes;

        namespace Company.RaskServer;

        public sealed class App : Component
        {
            // App-level head contributions splice into the framework-managed <head>
            // via the Component? Head override. Title is singleton — any page that
            // overrides Head with its own Title supersedes this fallback for the tab.
            protected override Component? Head => [
                Title()["Company.RaskServer"],
                Meta("utf-8"),
                Meta(Name: "viewport", Content: "width=device-width, initial-scale=1"),
                // Bootstrap 5.3 + Icons via Rask.Bootstrap (served from _content/Rask.Bootstrap).
                BootstrapStyles()
            ];

            protected override Component? Render() =>
                [
                    Doctype(),
                    Html("en")[
                        Head(),
                        Body()[
                            Nav()[
                                NavLink(HomePage())["Home"],
                                " | ",
                                NavLink(Counter())["Counter"],
                                " | ",
                                NavLink(Weather())["Weather"]{{greetingNav}}
                            ],
                            Hr(),
                            Router()
                        ]
                    ]
                ];
        }

        """;
    }

    private static string ServerNextSteps(string name, bool docker)
    {
        var steps = new StringBuilder();
        steps.Append("Created ").Append(name).Append(" (Rask server app).\n\nNext steps:\n");
        steps.Append("  cd ").Append(name).Append('\n');
        steps.Append("  rask dev            # run with hot reload (or: dotnet run)\n");
        if (docker)
        {
            steps.Append("  docker build -t ").Append(name.ToLowerInvariant()).Append(" .   # then: docker run -p 8080:8080 …\n");
        }

        return steps.ToString();
    }

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

    private const string CqrsGreetingQuery =
        """
        using Rask.Cqrs;

        namespace Company.RaskServer;

        // A CQRS query and its handler. Rask.Cqrs discovers the handler at build time (source-generated,
        // reflection-free) so a single AddRaskCqrs() in Program.cs registers it — no manual wiring here.
        // Dispatch it with IDispatcher.DispatchAsync(new GreetingQuery(...)); the result type is inferred
        // from IQuery<string>. Add more IQuery<T>/ICommand/ICommand<T> messages the same way. See docs/cqrs.md.
        public sealed record GreetingQuery(string Name) : IQuery<string>;

        public sealed class GreetingQueryHandler : IQueryHandler<GreetingQuery, string>
        {
            public Task<string> HandleAsync(GreetingQuery query, CancellationToken cancellationToken)
            {
                var name = string.IsNullOrWhiteSpace(query.Name) ? "world" : query.Name.Trim();
                return Task.FromResult($"Hello, {name}!");
            }
        }

        """;

    private const string CqrsGreetingPage =
        """
        using Rask.Cqrs;
        using Rask.Core.Routing;

        namespace Company.RaskServer;

        // Injects the umbrella IDispatcher and dispatches GreetingQuery — on mount, and again on each button
        // click. The awaited dispatch re-renders this component automatically, so there's no StateHasChanged()
        // by hand. This is the whole CQRS round-trip: a page sends a message, a handler (in
        // Cqrs/GreetingQuery.cs) answers it, decoupled from the UI. See docs/cqrs.md.
        [Route("/greeting")]
        public sealed class GreetingPage(IDispatcher dispatcher) : Component
        {
            private static readonly string[] Names = ["world", "Ada", "Grace", "Linus"];
            private int _index;
            private string _greeting = "";

            protected override async Task OnMountAsync() =>
                _greeting = await dispatcher.DispatchAsync(new GreetingQuery(Names[_index]), CancellationToken);

            private async Task GreetNextAsync()
            {
                _index = (_index + 1) % Names.Length;
                _greeting = await dispatcher.DispatchAsync(new GreetingQuery(Names[_index]), CancellationToken);
            }

            protected override Component? Render() =>
                [
                    H1()["CQRS greeting"],
                    P()["Each click dispatches a GreetingQuery through the mediator; a handler answers it."],
                    P(Id: "greeting", Class: "fs-4 fw-semibold")[_greeting],
                    BsButton(Color: BsColor.Primary, OnClickAsync: GreetNextAsync)["Greet the next name"]
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

    private const string ServerReadme =
        """
        # Company.RaskServer

        A server-side [Rask](https://github.com/pal-tamas/rask) app. The browser holds a thin
        client; renders and events flow over a WebSocket and Rask ships a minimal diff per update.

        > Scaffolded with `rask new` — Rask is the .NET One Person Framework.
        > For a client-side WebAssembly app instead, use `rask new --template wasm` (or `wasm-hosted`).

        ## Run

        ```bash
        rask dev        # hot reload (or: dotnet run)
        ```

        Then open the printed URL.

        ## Layout

        - `Program.cs` — host wiring: `AddRask()` + `UseRask<App>()`.
        - `App.cs` — the root component; renders the full page shell (`Doctype`/`Html`/`Head`/`Body`).
        - `HomePage.cs` (+ `HomePage.css`) — a routed page with co-located scoped styles.
        - `Counter.cs` — an interactive component.
        - `Weather.cs` / `LocalWeatherForecastService.cs` — data via DI.

        Add a full CRUD feature in one command: `rask generate feature Product --fields "Name:string,Price:decimal"`.

        Next steps: the [Rask docs](https://github.com/pal-tamas/rask/tree/main/docs).

        """;

    private const string ServerAgents =
        """
        # AGENTS.md — building this app with an AI assistant

        This is a **Rask** app. Rask is the .NET One Person Framework (a full-stack C# framework for .NET 10). This
        file tells AI coding assistants the conventions so generated code compiles and runs. Full docs:
        https://github.com/pal-tamas/rask/tree/main/docs

        ## Mental model
        - Components are **plain C# classes** deriving from `Component`. Override `Component? Render()`
          and return a tree of HTML built with **generated factory methods** — no `.razor`, no JSX.
        - The **same component code** runs server-rendered (live diff over WebSockets) or on WASM.

        ## The rules that matter
        - **Use factories, never `new`** for components: `Div(...)`, `Button(OnClick: ...)`. `new` outside the
          framework is a compile error (RASK014).
        - **Children go through the indexer**, not a constructor arg: `Div()[Span()["hi"], "text"]`. A bare
          `string` becomes a text node; pass a list directly for collections: `Ul()[items]`. `..` spread does not work.
        - **Props are factory parameters.** A nullable prop is optional; a non-nullable prop with no initializer is
          **required**. Inject services through the **constructor**, not settable properties.
        - **A page/root component renders the full shell**: `[Doctype(), Html(...)[Head(...), Body(...)]]` (RASK021).
          The framework injects its runtime `<script>` automatically.
        - **Text vs raw:** a bare string / `Text("..")` HTML-encodes; `Raw("..")` is verbatim (XSS risk).
        - Route with `[Route("/users/{id:int}")]` + `[RouteParam]`/`[QueryParam]`. Lifecycle: `OnMount*`,
          `OnPropsChanged*`, `OnRendered`, `OnUnmount*`. Navigate from event handlers via injected `Navigator`.

        ## Scaffolding — use the `rask` CLI
        - `rask generate page <Name>` / `rask generate component <Name>` scaffold a routed page / a component.
        - `rask generate feature <Name> <field:type> …` emits a full CQRS + EF Core CRUD vertical slice (entity,
          value objects, validation, list/create/edit pages, tests). Flags: `--bs`, `--modal`, `--soft-delete`,
          `--concurrency`, `--events`, `--outbox`, `--tests`. See docs/cli.md.
        - `rask dev` runs the app with hot reload; `rask new` scaffolds a project.

        If you hit a `RASKxxx` compile error, see https://github.com/pal-tamas/rask/blob/main/docs/diagnostics.md

        """;
}
