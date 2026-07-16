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
    public static ScaffoldResult GenerateWasmHosted(string targetDirectory, string name, bool auth, bool pwa, bool docker, string version)
    {
        var files = new List<(string Path, string Content)>
        {
            ($"{NameToken}.sln", WasmHostedSln),

            // Shared — the class library both the Client and the Server reference.
            ($"{NameToken}.Shared/{NameToken}.Shared.csproj", SharedCsproj),
            ($"{NameToken}.Shared/Contracts.cs", auth ? WasmHostedSharedContractsAuth : WasmHostedSharedContracts),

            // Client — the browser-WASM SPA.
            ($"{NameToken}.Client/{NameToken}.Client.csproj", WasmHostedClientCsproj(version)),
            ($"{NameToken}.Client/Program.cs", WasmHostedClientProgram(auth, pwa)),
            ($"{NameToken}.Client/App.cs", WasmHostedClientApp),
            ($"{NameToken}.Client/wwwroot/index.html", WasmIndexHtml(pwa)),
            ($"{NameToken}.Client/runtimeconfig.template.json", WasmRuntimeConfig),

            // Server — the ASP.NET host that serves the baked WASM bundle.
            ($"{NameToken}.Server/{NameToken}.Server.csproj", WasmHostedServerCsproj(version)),
            ($"{NameToken}.Server/Program.cs", WasmHostedServerProgram(auth)),
            ($"{NameToken}.Server/Properties/launchSettings.json", WasmHostedServerLaunchSettings),
        };

        if (auth)
        {
            files.Add(($"{NameToken}.Client/Auth/Auth.cs", WasmHostedClientAuth));
            files.Add(($"{NameToken}.Client/Auth/LoginPage.cs", WasmHostedClientLoginPage));
            files.Add(($"{NameToken}.Client/Auth/MembersPage.cs", WasmHostedClientMembersPage));
            files.Add(($"{NameToken}.Server/Auth/CredentialStore.cs", WasmHostedServerCredentialStore));
        }

        if (pwa)
        {
            files.Add(($"{NameToken}.Client/wwwroot/icon.svg", IconSvg));
        }

        if (docker)
        {
            files.Add(("Dockerfile", WasmHostedDockerfile));
            files.Add((".dockerignore", DockerIgnore));
        }

        var scaffoldFiles = Materialize(targetDirectory, name, files);

        return new ScaffoldResult(scaffoldFiles, WasmHostedNextSteps(name, docker))
        {
            Packages = ["Rask.Wasm", "Rask.Bootstrap", "Rask.Wasm.Hosting"],
            // No root csproj — restore (and the overwrite guard) target the solution, which pulls all three.
            RestoreTarget = $"{name}.sln",
        };
    }

    // The welcome shell + home page are exactly the server/wasm one, only in the .Client namespace so the
    // Server's cross-project reference and the client's own types line up. Reuse keeps the three in sync.
    private static string WasmHostedClientApp =>
        AppCs.Replace($"namespace {NameToken};", $"namespace {NameToken}.Client;", StringComparison.Ordinal);

    private static string WasmHostedClientProgram(bool auth, bool pwa)
    {
        var sb = new StringBuilder();
        sb.Append($"using {NameToken}.Client;\n");
        sb.Append("using Microsoft.Extensions.DependencyInjection;\n");
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

            // PathBase is auto-detected from <base href>. Publish with /p:RaskPathBase=/myapp
            // for sub-path deploys, or override via CreateDefault(o => o.PathBase = "/myapp").
            var host = WasmHostBuilder.CreateDefault();

            // Same-origin HttpClient so the client can call its own host's API endpoints.
            host.Services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri(WasmHostBuilder.BaseAddress) });

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

                // Hydrates the user from the host's /api/me (HttpOnly cookie); WasmLoginService drives sign-in/out.
                host.Services.AddSingleton<ApiUserProvider>();
                host.Services.AddSingleton<IUserProvider>(sp => sp.GetRequiredService<ApiUserProvider>());
                host.Services.AddSingleton<WasmLoginService>();

                """.TrimStart('\n'));
        }

        sb.Append("\nawait host.RunAsync<App>();\n");
        return sb.ToString();
    }

    private static string WasmHostedServerProgram(bool auth)
    {
        var sb = new StringBuilder();
        if (auth)
        {
            sb.Append($"using {NameToken}.Server;\n");
            sb.Append($"using {NameToken}.Shared;\n");
            sb.Append("using System.Security.Claims;\n");
            sb.Append("using Microsoft.AspNetCore.Authentication;\n");
            sb.Append("using Microsoft.AspNetCore.Authentication.Cookies;\n");
        }

        sb.Append("using Rask.Wasm.Hosting;\n");

        sb.Append("""

            var builder = WebApplication.CreateBuilder(args);

            // Opt into brotli + gzip response compression for the published wwwroot (.wasm / .js / .json).
            builder.Services.AddRask();

            """.TrimStart('\n'));

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

        sb.Append("""

            var app = builder.Build();

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

        sb.Append("""

            // Serve the baked WASM bundle: UseDefaultFiles + UseStaticFiles (pre-compressed .br/.gz siblings)
            // + a SPA fallback to index.html for client-side routes. Non-generic on purpose — the host serves
            // static files and never runs the components, so it needs no reference to the client's App type.
            app.UseRask();

            app.Run();

            """.TrimStart('\n'));

        return sb.ToString();
    }

    private static string WasmHostedNextSteps(string name, bool docker)
    {
        var steps = new StringBuilder();
        steps.Append("Created ").Append(name).Append(" (Rask WASM + ASP.NET host).\n\nNext steps:\n");
        steps.Append("  cd ").Append(name).Append('\n');
        steps.Append("  rask dev --project ").Append(name).Append(".Server   # run the host with hot reload\n");
        if (docker)
        {
            steps.Append("  docker build -t ").Append(name.ToLowerInvariant()).Append(" .   # then: docker run -p 8080:8080 …\n");
        }

        return steps.ToString();
    }

    // ---- wasm-hosted template files ----

    private const string WasmHostedSln =
        "\nMicrosoft Visual Studio Solution File, Format Version 12.00\n"
        + "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Company.RaskServer.Client\", \"Company.RaskServer.Client\\Company.RaskServer.Client.csproj\", \"{B1A2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D}\"\nEndProject\n"
        + "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Company.RaskServer.Server\", \"Company.RaskServer.Server\\Company.RaskServer.Server.csproj\", \"{C1A2B3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5E}\"\nEndProject\n"
        + "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Company.RaskServer.Shared\", \"Company.RaskServer.Shared\\Company.RaskServer.Shared.csproj\", \"{D1A2B3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5F}\"\nEndProject\n"
        + "Global\n"
        + "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\n"
        + "\t\tDebug|Any CPU = Debug|Any CPU\n"
        + "\t\tRelease|Any CPU = Release|Any CPU\n"
        + "\tEndGlobalSection\n"
        + "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\n"
        + "\t\t{B1A2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\n"
        + "\t\t{B1A2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D}.Debug|Any CPU.Build.0 = Debug|Any CPU\n"
        + "\t\t{B1A2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D}.Release|Any CPU.ActiveCfg = Release|Any CPU\n"
        + "\t\t{B1A2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D}.Release|Any CPU.Build.0 = Release|Any CPU\n"
        + "\t\t{C1A2B3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5E}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\n"
        + "\t\t{C1A2B3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5E}.Debug|Any CPU.Build.0 = Debug|Any CPU\n"
        + "\t\t{C1A2B3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5E}.Release|Any CPU.ActiveCfg = Release|Any CPU\n"
        + "\t\t{C1A2B3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5E}.Release|Any CPU.Build.0 = Release|Any CPU\n"
        + "\t\t{D1A2B3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5F}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\n"
        + "\t\t{D1A2B3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5F}.Debug|Any CPU.Build.0 = Debug|Any CPU\n"
        + "\t\t{D1A2B3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5F}.Release|Any CPU.ActiveCfg = Release|Any CPU\n"
        + "\t\t{D1A2B3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5F}.Release|Any CPU.Build.0 = Release|Any CPU\n"
        + "\tEndGlobalSection\n"
        + "\tGlobalSection(SolutionProperties) = preSolution\n"
        + "\t\tHideSolutionNode = FALSE\n"
        + "\tEndGlobalSection\n"
        + "EndGlobal\n";

    private const string SharedCsproj =
        """
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>

        </Project>

        """;

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

    // NOTE: the property block mirrors the standalone `wasm` template's WasmCsproj (ProjectGenerator.Wasm.cs) —
    // keep the two in sync. The hosted client differs only in referencing the Shared project (and never the
    // --auth JSInterop/Authorization refs — hosted auth is cookie-based, so it needs neither).
    private static string WasmHostedClientCsproj(string version) =>
        $"""
        <Project Sdk="Microsoft.NET.Sdk.WebAssembly">

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

          <ItemGroup>
            <PackageReference Include="Rask.Wasm" Version="{version}"/>
            <PackageReference Include="Rask.Bootstrap" Version="{version}"/>
            <ProjectReference Include="..\Company.RaskServer.Shared\Company.RaskServer.Shared.csproj"/>
          </ItemGroup>

        </Project>

        """;

    private static string WasmHostedServerCsproj(string version) =>
        $"""
        <Project Sdk="Microsoft.NET.Sdk.Web">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
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
            <PackageReference Include="Rask.Wasm.Hosting" Version="{version}"/>
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

        namespace Company.RaskServer.Server;

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

        namespace Company.RaskServer.Client;

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

    private const string WasmHostedClientLoginPage =
        """
        using Microsoft.AspNetCore.Authorization;
        using Rask.Core.Components;
        using Rask.Core.Routing;

        namespace Company.RaskServer.Client;

        [Route("login")]
        [AllowAnonymous]
        public sealed class LoginPage(WasmLoginService login) : Component
        {
            private readonly LoginModel _model = new();
            private string? _error;

            [QueryParam] public string? ReturnUrl { get; set; }

            protected override Component? Render() =>
                Div(Style: "max-width:22rem;margin:3rem auto;font-family:system-ui")[
                    H1()["Sign in"],
                    _error is null ? null : Div(Style: "color:#b00020")[_error],
                    Form(_model, OnValidSubmitAsync: SubmitAsync)[
                        Div()[Label("username")["Username"], Input(() => _model.Username, Id: "username")],
                        Div()[Label("password")["Password"], Input(() => _model.Password, Id: "password", Type: InputType.Password)],
                        Button("submit", Id: "login-submit")["Sign in"]
                    ],
                    P()["Try alice / password (user) or root / password (admin)."]
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

        namespace Company.RaskServer.Client;

        // On WASM there's no server route guard — the Authorize component gates the content off the principal
        // (hydrated from /api/me). The signed-in view is a child component so it reads the fresh principal when
        // the gate opens after sign-in.
        [Route("members")]
        [AllowAnonymous]
        public sealed class MembersPage : Component
        {
            protected override Component? Render() =>
                Div(Style: "max-width:32rem;margin:3rem auto;font-family:system-ui")[
                    Authorize(
                        NotAuthorized: P()["Please ", NavLink(Href: Routes.LoginPage())["sign in"], "."])[MemberContent()]
                ];
        }

        public sealed class MemberContent(WasmLoginService login, IUserProvider userProvider) : Component
        {
            protected override Component? Render() =>
                [
                    H1()[$"Welcome, {userProvider.Current.Identity?.Name}"],
                    Authorize(Roles: ["admin"])[
                        Div(Style: "color:#7a5c00")["🔑 You have admin access."]],
                    Button(Id: "logout", OnClickAsync: login.LogoutAsync)["Sign out"]
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
        EXPOSE 8080
        # The Server calls UseHttpsRedirection(); inside the container no HTTPS port is configured,
        # so it no-ops. Terminate TLS at your reverse proxy / ingress and forward plain HTTP to 8080.
        ENTRYPOINT ["dotnet", "Company.RaskServer.Server.dll"]

        """;
}
