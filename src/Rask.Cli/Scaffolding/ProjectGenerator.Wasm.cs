using System.Text;

namespace Rask.Cli.Scaffolding;

// The wasm template: a standalone browser-WASM SPA.
internal static partial class ProjectGenerator
{
    /// <summary>Generates the <c>wasm</c> template (a standalone browser-WASM SPA) into <paramref name="targetDirectory"/>.</summary>
    public static ScaffoldResult GenerateWasm(string targetDirectory, string name, bool auth, bool pwa, bool docker, string version, bool bootstrap = true)
    {
        var files = new List<(string Path, string Content)>
        {
            ($"{NameToken}.csproj", WasmCsproj(auth, bootstrap, version)),
            ("Program.cs", WasmProgram(auth, pwa)),
            // The shell + welcome page are identical to the server template's (Features/Shared + Features/Home).
            ("Features/Shared/App.cs", AppShellCs(bootstrap)),
            ("Features/Home/HomePage.cs", HomePageCs(bootstrap)),
            ("wwwroot/index.html", WasmIndexHtml(pwa)),
            ("runtimeconfig.template.json", WasmRuntimeConfig),
        };

        if (auth)
        {
            files.Add(("Features/Auth/Auth.cs", WasmAuth));
            files.Add(("Features/Auth/LoginPage.cs", WasmLoginPage));
            files.Add(("Features/Auth/MembersPage.cs", WasmMembersPage));
        }

        if (pwa)
        {
            files.Add(("wwwroot/icon.svg", IconSvg));
        }

        if (docker)
        {
            files.Add(("Dockerfile", WasmDockerfile));
            files.Add((".dockerignore", DockerIgnore));
            files.Add(("nginx.conf", WasmNginxConf));
        }

        files.AddRange(ProjectHygiene($"{NameToken}.csproj"));

        var scaffoldFiles = Materialize(targetDirectory, name, files);

        return new ScaffoldResult(scaffoldFiles, WasmNextSteps(name, docker))
        {
            Packages = bootstrap ? ["Rask.Wasm", "Rask.Bootstrap"] : ["Rask.Wasm"],
        };
    }

    // The JWT auth scaffold uses IJSRuntime (localStorage) + [AllowAnonymous]. On a browser-wasm app there's
    // no Microsoft.AspNetCore.App framework reference to supply them and the transitive compile assets from
    // Rask.Core don't flow through the published package chain, so the --auth scaffold references them directly.
    //
    // This MUST match what Directory.Packages.props pins, because Rask.Wasm references the same two packages
    // and its nuspec therefore demands `>= <that pin>`. Scaffolding a lower version puts the generated project
    // *below* its own dependency and NuGet reports a downgrade (NU1605) — an error under -warnaserror.
    // ProjectGeneratorTests.Wasm_auth_framework_version_matches_the_repo_pin holds the two in sync.
    internal const string AspNetCoreFrameworkVersion = "10.0.11";

    // The WebAssembly SDK <PropertyGroup> — byte-identical for the standalone `wasm` template and the
    // `wasm-hosted` client project. Shared here so the two csproj builders (WasmCsproj and
    // WasmHostedClientCsproj) can never drift.
    internal const string WasmSdkPropertyGroup =
        """
          <PropertyGroup>
            <TargetFramework>net10.0-browser</TargetFramework>
            <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
            <OutputType>Exe</OutputType>
            <UseAppHost>false</UseAppHost>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
            <!-- Rask WASM marker (gates the framework's wwwroot staging + scoped-asset bake). -->
            <RaskWasm>true</RaskWasm>
            <!-- Fingerprint framework assets + fill the index.html import map / preload placeholders on
                 publish so static-host (GitHub Pages) redeploys stay subresource-integrity-safe. -->
            <OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>
            <!-- Full WASM AOT is opt-in: publish with -p:RaskWasmAot=true (needs the wasm-tools workload)
                 to AOT-compile IL->WASM; the default keeps the Mono interpreter. Both are gated off the fast
                 no-native build: the SDK's runtime-pack default for this property is empty, so even 'false'
                 is a relink trigger conflicting with -p:WasmBuildNative=false. -->
            <RunAOTCompilation Condition=" '$(RaskWasmAot)' == 'true' ">true</RunAOTCompilation>
            <RunAOTCompilation Condition=" '$(RaskWasmAot)' != 'true' and '$(WasmBuildNative)' != 'false' ">false</RunAOTCompilation>
            <!-- Trimming is trim-safe: page types reach the runtime via the route registry's generated
                 module initialiser, which emits a [DynamicDependency] per registered page. -->
            <PublishTrimmed>true</PublishTrimmed>
            <TrimMode>full</TrimMode>
            <!-- Drops the ~2.6 MB of ICU data under _framework/icudt*.dat. Remove this if your app
                 formats culture-sensitive values (dates, numbers, currency). Gated off the fast
                 no-native build (-p:WasmBuildNative=false): the SDK forces a native relink when
                 InvariantGlobalization=true, so the two conflict, and it's irrelevant with no runtime. -->
            <InvariantGlobalization Condition=" '$(WasmBuildNative)' != 'false' ">true</InvariantGlobalization>
            <!-- IL2104 comes from Microsoft.JSInterop's reflection-driven [JSInvokable] scanner; apps
                 that only INVOKE JS never hit it. If you mark methods [JSInvokable], add a
                 [DynamicDependency] on them (standard Blazor WASM mitigation) instead of suppressing. -->
            <NoWarn>$(NoWarn);IL2104</NoWarn>
          </PropertyGroup>
        """;

    private static string WasmCsproj(bool auth, bool bootstrap, string version)
    {
        var bootstrapRef = bootstrap
            ? $"\n    <PackageReference Include=\"Rask.Bootstrap\" Version=\"{version}\"/>"
            : "";
        var authRefs = auth
            ? $"""

                    <PackageReference Include="Microsoft.JSInterop" Version="{AspNetCoreFrameworkVersion}"/>
                    <PackageReference Include="Microsoft.AspNetCore.Authorization" Version="{AspNetCoreFrameworkVersion}"/>
                """.TrimEnd()
            : "";
        return $"""
        <Project Sdk="Microsoft.NET.Sdk.WebAssembly">

        {WasmSdkPropertyGroup}

          <ItemGroup>
            <PackageReference Include="Rask.Wasm" Version="{version}"/>{bootstrapRef}{authRefs}
          </ItemGroup>

        </Project>

        """;
    }

    private static string WasmProgram(bool auth, bool pwa)
    {
        var sb = new StringBuilder();
        sb.Append("using Company.RaskServer.Features.Shared;\n"); // App lives in the Features/Shared bucket.
        if (auth)
        {
            // Only the --auth block registers services, so these would otherwise be unused usings.
            sb.Append("using Company.RaskServer.Features.Auth;\n");
            sb.Append("using Microsoft.Extensions.DependencyInjection;\n");
        }

        sb.Append("using Rask.Wasm;\n");
        if (auth)
        {
            sb.Append("using Rask.Core.Authentication;\n");
        }

        if (pwa)
        {
            sb.Append("using Rask.Core.Browser;\n");
        }

        sb.Append("""

            // PathBase is auto-detected at boot from <base href>. For sub-path deploys
            // (e.g. GH Pages at https://<user>.github.io/<repo>/), publish with
            // /p:RaskPathBase=/<repo> — the framework rewrites the published
            // index.html's <base href> so the runtime picks up the prefix on first paint
            // and head-emitted asset URLs are scoped under /<repo>/_rask/a/{hash}.{ext}. Override
            // explicitly via WasmHostBuilder.CreateDefault(o => o.PathBase = "/myapp")
            // if you need to set it from .NET code.
            var host = WasmHostBuilder.CreateDefault();

            """.TrimStart('\n'));

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

                // A standalone SPA has no host of its own — point this at YOUR auth API (CORS-enabled).
                const string authApiBaseAddress = "https://api.example.com/"; // TODO: your auth API
                host.Services.AddSingleton<TokenStore>();
                host.Services.AddSingleton(sp =>
                    new HttpClient(new BearerTokenHandler(sp.GetRequiredService<TokenStore>()) { InnerHandler = new HttpClientHandler() })
                    {
                        BaseAddress = new Uri(authApiBaseAddress)
                    });
                host.Services.AddSingleton<JwtUserProvider>();
                host.Services.AddSingleton<IUserProvider>(sp => sp.GetRequiredService<JwtUserProvider>());
                host.Services.AddSingleton<JwtLoginService>();

                """.TrimStart('\n'));
        }

        sb.Append("\nawait host.RunAsync<App>();\n");
        return sb.ToString();
    }

    private static string WasmNextSteps(string name, bool docker)
    {
        var steps = new StringBuilder();
        steps.Append("Created ").Append(name).Append(" (Rask browser-WASM SPA).\n\nNext steps:\n");
        steps.Append("  cd ").Append(name).Append('\n');
        steps.Append("  rask dev            # run with hot reload (or: dotnet run)\n");
        if (docker)
        {
            steps.Append("  docker build -t ").Append(name.ToLowerInvariant()).Append(" .   # then: docker run -p 8080:80 …\n");
        }

        return steps.ToString();
    }

    // ---- wasm-only template files ----

    private static string WasmIndexHtml(bool pwa)
    {
        var swBlock = pwa
            ? """


                <!-- PWA: register Rask's default service worker (offline app-shell cache + Web Push). Resolves
                     relative to <base href>, so it works at the origin root and under a sub-path deploy. -->
                <script>
                    if ("serviceWorker" in navigator) {
                        window.addEventListener("load", function () {
                            var base = document.querySelector("base");
                            var scope = base ? new URL(base.href).pathname : "/";
                            navigator.serviceWorker.register(scope + "rask-sw.js").catch(function () { });
                        });
                    }
                </script>
                """.TrimEnd()
            : "";

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8"/>
                <!-- <base href="/"> forces relative URLs (main.js, _framework/*) to resolve from the origin
                     root regardless of the current document path. GH Pages deploys rewrite this to /<repo>/. -->
                <base href="/"/>
                <meta content="width=device-width, initial-scale=1" name="viewport"/>
                <title>Rask WASM</title>
                <!-- Asset-fingerprinting placeholders (OverrideHtmlAssetPlaceholders): on publish the SDK fills
                     this import map with content-hashed framework asset URLs + integrity hashes and schedules
                     the runtime download via the preload link. Content-hashed URLs keep static-host redeploys
                     cache-safe. -->
                <link rel="preload" id="webassembly"/>
                <script type="importmap"></script>
                <!-- Inline data-URI favicon (the Rask bolt) so the boot screen is branded with no external file. -->
                <link href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='22 6 80 108'%3E%3ClinearGradient id='b' x1='0' y1='0' x2='1' y2='1'%3E%3Cstop offset='0' stop-color='%237C3AED'/%3E%3Cstop offset='1' stop-color='%23512BD4'/%3E%3C/linearGradient%3E%3Cpath d='M72 14 L30 64 L54 64 L48 106 L94 54 L68 54 Z' fill='url(%23b)'/%3E%3C/svg%3E" rel="icon"
                      type="image/svg+xml"/>
                <style id="rask-scoped"></style>
                <style>
                    .rask-boot {
                        position: fixed;
                        inset: 0;
                        display: flex;
                        flex-direction: column;
                        align-items: center;
                        justify-content: center;
                        gap: 1.25rem;
                        font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
                        color: #512BD4;
                        background: #faf9fe;
                    }

                    .rask-boot svg {
                        width: 64px;
                        height: 64px;
                        animation: rask-pulse 1.4s ease-in-out infinite;
                    }

                    .rask-boot .rask-spin {
                        width: 26px;
                        height: 26px;
                        border-radius: 50%;
                        border: 3px solid rgba(124, 58, 237, 0.22);
                        border-top-color: #7C3AED;
                        animation: rask-spin 0.8s linear infinite;
                    }

                    @keyframes rask-spin {
                        to {
                            transform: rotate(360deg);
                        }
                    }

                    @keyframes rask-pulse {
                        0%, 100% {
                            opacity: 1;
                        }
                        50% {
                            opacity: 0.55;
                        }
                    }
                </style>
            </head>
            <body data-rask-root>
            <div class="rask-boot">
                <svg aria-label="Rask" role="img" viewBox="22 6 80 108" xmlns="http://www.w3.org/2000/svg">
                    <linearGradient id="rask-boot-bolt" x1="0" x2="1" y1="0" y2="1">
                        <stop offset="0" stop-color="#7C3AED"/>
                        <stop offset="1" stop-color="#512BD4"/>
                    </linearGradient>
                    <path d="M72 14 L30 64 L54 64 L48 106 L94 54 L68 54 Z" fill="url(#rask-boot-bolt)"/>
                </svg>
                <div class="rask-spin"></div>
            </div>
            <script src="main.js" type="module"></script>{{swBlock}}
            </body>
            </html>

            """;
    }

    private const string WasmRuntimeConfig =
        """
        {
          "wasmHostProperties": {
            "perHostConfig": [
              {
                "name": "browser",
                "html-path": "index.html",
                "Host": "browser"
              }
            ]
          }
        }

        """;

    private const string WasmNginxConf =
        """
        server {
            # 8080, not 80, so this image matches the server and wasm-hosted templates — `rask deploy`
            # points the proxy and its readiness probe at one container port for every template.
            listen 8080;
            root /usr/share/nginx/html;
            index index.html;

            # A readiness endpoint, so a deployed bundle can be health-gated like any other Rask app.
            # A static bundle has nothing to report but "nginx is serving", which is exactly the
            # question the deploy's blue-green swap asks before switching traffic.
            location = /health {
                access_log off;
                add_header Content-Type text/plain;
                return 200 'ok';
            }

            # Serve the *.gz siblings the publish step baked next to each asset. (Rask also bakes
            # *.br, but the stock nginx:alpine image has no brotli module; gzip is universally
            # accepted, so gzip_static alone keeps transfers small.)
            gzip_static on;

            # The Mono runtime .wasm must be served as application/wasm or the browser refuses to
            # streaming-compile it. nginx's default mime.types omits this entry on older images.
            types {
                application/wasm wasm;
            }

            # SPA fallback: unknown paths are client-side routes, so serve the app shell.
            location / {
                try_files $uri $uri/ /index.html;
            }
        }

        """;

    private const string WasmDockerfile =
        """
        # Multi-stage build: publish the standalone WASM bundle on the .NET SDK image, then serve
        # the static output from a tiny nginx image. A standalone Rask SPA has no ASP.NET host of
        # its own — it's plain static files, so any static-file server works.
        FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
        # The browser-wasm target needs the wasm-tools workload to publish.
        RUN dotnet workload install wasm-tools
        WORKDIR /src

        # Restore first (cached layer): only the csproj invalidates it, so code edits reuse the cache.
        COPY ["Company.RaskServer.csproj", "./"]
        RUN dotnet restore
        COPY . .
        RUN dotnet publish "Company.RaskServer.csproj" -c Release -o /app --no-restore

        FROM nginx:alpine
        COPY --from=build /app/wwwroot /usr/share/nginx/html
        COPY nginx.conf /etc/nginx/conf.d/default.conf
        EXPOSE 8080

        """;

    private const string WasmAuth =
        """
        using System.Net.Http.Headers;
        using System.Net.Http.Json;
        using System.Security.Claims;
        using System.Text.Json.Serialization;
        using Microsoft.JSInterop;
        using Rask.Core.Authentication;
        using Rask.Core.Routing;

        namespace Company.RaskServer.Features.Auth;

        public sealed record LoginRequest(
            [property: JsonPropertyName("username")] string Username,
            [property: JsonPropertyName("password")] string Password);

        public sealed record TokenResponse([property: JsonPropertyName("token")] string Token);

        public sealed record MeDto(
            [property: JsonPropertyName("name")] string Name,
            [property: JsonPropertyName("roles")] string[] Roles);

        public sealed class LoginModel
        {
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
        }

        [JsonSerializable(typeof(LoginRequest))]
        [JsonSerializable(typeof(TokenResponse))]
        [JsonSerializable(typeof(MeDto))]
        public partial class AuthJson : JsonSerializerContext;

        // Bearer JWT in localStorage (survives refresh) + an in-memory copy the handler reads synchronously.
        // SECURITY: a token in localStorage is plaintext and readable by ANY script on the page (XSS), so this
        // scaffolded store is a development-grade floor. Before production, prefer an HttpOnly cookie (the token
        // never reaches JS) or encrypt at rest with ProtectedTokenStore — see docs/authentication.md. The
        // WarnOnce below logs a one-time reminder to the browser console while this plaintext store is in use.
        public sealed class TokenStore(IJSRuntime js)
        {
            private bool _warned;

            public string? Token { get; private set; }

            public async Task InitAsync()
            {
                Token = await js.InvokeAsync<string?>("localStorage.getItem", "rask.jwt");
                if (Token is not null)
                {
                    await WarnOnceAsync();
                }
            }

            public async Task SetAsync(string token)
            {
                Token = token;
                await js.InvokeVoidAsync("localStorage.setItem", "rask.jwt", token);
                await WarnOnceAsync();
            }

            public async Task ClearAsync()
            {
                Token = null;
                await js.InvokeVoidAsync("localStorage.removeItem", "rask.jwt");
            }

            // One-time console warning so a scaffold shipped to production unchanged surfaces the risk.
            // Delete this (and harden the store) once you've moved to an HttpOnly cookie or ProtectedTokenStore.
            private async Task WarnOnceAsync()
            {
                if (_warned)
                {
                    return;
                }

                _warned = true;
                await js.InvokeVoidAsync("console.warn",
                    "Rask: the bearer token is stored in plaintext localStorage and is readable by any script "
                    + "(XSS risk). This is a development floor — for production use an HttpOnly cookie or encrypt "
                    + "the token at rest (ProtectedTokenStore). See docs/authentication.md.");
            }
        }

        public sealed class BearerTokenHandler(TokenStore tokens) : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (tokens.Token is { } token)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                return base.SendAsync(request, ct);
            }
        }

        public sealed class JwtUserProvider(HttpClient http, TokenStore tokens) : IUserProvider
        {
            private ClaimsPrincipal _current = new(new ClaimsIdentity());
            public ClaimsPrincipal Current => _current;
            public bool IsLoading { get; private set; }
            public event Action? Changed;

            public async Task EnsureLoadedAsync()
            {
                IsLoading = true; // bridge the anonymous→authed flash (LoadAsync's finally clears it)
                await tokens.InitAsync();
                await LoadAsync();
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
                    if (tokens.Token is null)
                    {
                        _current = new ClaimsPrincipal(new ClaimsIdentity());
                        return;
                    }

                    // GetAsync (not GetFromJsonAsync): a 204 No Content would make GetFromJsonAsync throw a
                    // JsonException on the empty body; treat anything but a 200-with-body as anonymous.
                    using var resp = await http.GetAsync("api/me");
                    var me = resp.StatusCode == System.Net.HttpStatusCode.OK
                        ? await resp.Content.ReadFromJsonAsync(AuthJson.Default.MeDto)
                        : null;
                    _current = me is { Name: { } name }
                        ? new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.Name, name), .. me.Roles.Select(r => new Claim(ClaimTypes.Role, r))], "jwt"))
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

        public sealed class JwtLoginService(HttpClient http, TokenStore tokens, IUserProvider users, Navigator nav)
        {
            public async Task<bool> LoginAsync(string username, string password, string? returnUrl)
            {
                var resp = await http.PostAsJsonAsync("api/login", new LoginRequest(username, password), AuthJson.Default.LoginRequest);
                if (!resp.IsSuccessStatusCode) return false;
                var dto = await resp.Content.ReadFromJsonAsync(AuthJson.Default.TokenResponse);
                if (dto is null) return false;
                await tokens.SetAsync(dto.Token);
                await users.RefreshAsync();
                // Open-redirect guard: an attacker-supplied returnUrl must never navigate off-origin.
                nav.NavigateTo(LocalUrl.Sanitize(returnUrl ?? "/members"));
                return true;
            }

            public async Task LogoutAsync()
            {
                nav.NavigateTo(Routes.LoginPage());
                await tokens.ClearAsync();
                await users.RefreshAsync();
            }
        }

        """;

    private const string WasmLoginPage =
        """
        using Microsoft.AspNetCore.Authorization;
        using Rask.Core.Components;
        using Rask.Core.Routing;

        namespace Company.RaskServer.Features.Auth;

        [AllowAnonymous]
        [Route("login")]
        public sealed partial class LoginPage(JwtLoginService login) : Component
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
                        Button("submit", Id: "login-submit")["Sign in"]
                    ]
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

    private const string WasmMembersPage =
        """
        using Microsoft.AspNetCore.Authorization;
        using Rask.Core.Authentication;
        using Rask.Core.Components;
        using Rask.Core.Routing;

        namespace Company.RaskServer.Features.Auth;

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

        public sealed partial class MemberContent(JwtLoginService login, IUserProvider userProvider) : Component
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
}
