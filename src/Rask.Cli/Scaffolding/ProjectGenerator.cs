using System.Text;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// Generates a whole project directly (the CLI is the scaffolding authority — no <c>dotnet new</c> /
/// Rask.Templates). Each template is hand-ported here: files are emitted with the placeholder namespace
/// <c>Company.RaskServer</c> and a final pass rewrites it (and the csproj filename) to the app name, so the
/// content reads exactly like the source template. Flag conditionals (<c>--auth</c>/<c>--pwa</c>/<c>--cqrs</c>/
/// <c>--docker</c>) are generation logic, not <c>#if</c> markers. Package references are pinned to
/// <paramref name="version"/> (the CLI's own version).
/// </summary>
internal static class ProjectGenerator
{
    private const string NameToken = "Company.RaskServer";

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

        // One replacement pass: the placeholder namespace + the csproj filename become the app name.
        var scaffoldFiles = files.Select(f => new ScaffoldFile(
            System.IO.Path.Combine(targetDirectory, f.Path.Replace(NameToken, name, StringComparison.Ordinal)),
            f.Content.Replace(NameToken, name, StringComparison.Ordinal))).ToList();

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

    /// <summary>Generates the <c>wasm</c> template (a standalone browser-WASM SPA) into <paramref name="targetDirectory"/>.</summary>
    public static ScaffoldResult GenerateWasm(string targetDirectory, string name, bool auth, bool pwa, bool docker, string version)
    {
        var files = new List<(string Path, string Content)>
        {
            ($"{NameToken}.csproj", WasmCsproj(auth, version)),
            ("Program.cs", WasmProgram(auth, pwa)),
            // The root shell is identical to the server template's (Home | Counter | Weather, no CQRS nav).
            ("App.cs", ServerApp(cqrs: false)),
            ("HomePage.cs", HomePage),
            ("HomePage.css", HomePageCss),
            ("Counter.cs", CounterCs),
            ("Weather.cs", WeatherCs),
            ("WeatherForecast.cs", WeatherForecastCs),
            ("LocalWeatherForecastService.cs", LocalWeatherServiceCs),
            ("wwwroot/index.html", WasmIndexHtml(pwa)),
            ("runtimeconfig.template.json", WasmRuntimeConfig),
            ("README.md", WasmReadme),
            ("AGENTS.md", WasmAgents),
        };

        if (auth)
        {
            files.Add(("Auth/Auth.cs", WasmAuth));
            files.Add(("Auth/LoginPage.cs", WasmLoginPage));
            files.Add(("Auth/MembersPage.cs", WasmMembersPage));
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

        var scaffoldFiles = files.Select(f => new ScaffoldFile(
            System.IO.Path.Combine(targetDirectory, f.Path.Replace(NameToken, name, StringComparison.Ordinal)),
            f.Content.Replace(NameToken, name, StringComparison.Ordinal))).ToList();

        return new ScaffoldResult(scaffoldFiles, WasmNextSteps(name, docker))
        {
            Packages = ["Rask.Wasm", "Rask.Bootstrap"],
        };
    }

    // The JWT auth scaffold uses IJSRuntime (localStorage) + [AllowAnonymous]. On a browser-wasm app there's
    // no Microsoft.AspNetCore.App framework reference to supply them and the transitive compile assets from
    // Rask.Core don't flow through the published package chain, so the --auth scaffold references them directly.
    private const string AspNetCoreFrameworkVersion = "10.0.9";

    private static string WasmCsproj(bool auth, string version)
    {
        var authRefs = auth
            ? $"""

                    <PackageReference Include="Microsoft.JSInterop" Version="{AspNetCoreFrameworkVersion}"/>
                    <PackageReference Include="Microsoft.AspNetCore.Authorization" Version="{AspNetCoreFrameworkVersion}"/>
                """.TrimEnd()
            : "";
        return $"""
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
            <PackageReference Include="Rask.Bootstrap" Version="{version}"/>{authRefs}
          </ItemGroup>

        </Project>

        """;
    }

    private static string WasmProgram(bool auth, bool pwa)
    {
        var sb = new StringBuilder();
        sb.Append("using Company.RaskServer;\n");
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

            // PathBase is auto-detected at boot from <base href>. For sub-path deploys
            // (e.g. GH Pages at https://<user>.github.io/<repo>/), publish with
            // /p:RaskPathBase=/<repo> — the framework rewrites the published
            // index.html's <base href> so the runtime picks up the prefix on first paint
            // and head-emitted asset URLs are scoped under /<repo>/_rask/a/{hash}.{ext}. Override
            // explicitly via WasmHostBuilder.CreateDefault(o => o.PathBase = "/myapp")
            // if you need to set it from .NET code.
            var host = WasmHostBuilder.CreateDefault();

            host.Services.AddSingleton<IWeatherForecastService, LocalWeatherForecastService>();

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

    /// <summary>
    /// Generates the <c>native</c> template (a WebView-hybrid iOS + Android app hosting a Rask app) into
    /// <paramref name="targetDirectory"/>. <paramref name="host"/> is <c>"local"</c> (the Rask component code
    /// runs in-process on the device) or <c>"server"</c> (a thin native shell over a remote Rask Server).
    /// </summary>
    public static ScaffoldResult GenerateNative(string targetDirectory, string name, string host, string version)
    {
        var isLocal = string.Equals(host, "local", StringComparison.Ordinal);

        // Shared files (both hosts): the multi-targeted csproj, both platform manifests, the iOS entry point,
        // and the docs. The platform manifests carry an inline IsLocal conditional (the native device
        // permissions) resolved per host — see NativeInfoPlist / NativeAndroidManifest.
        var files = new List<(string Path, string Content)>
        {
            ($"{NameToken}.csproj", NativeCsproj(version)),
            ("Platforms/Android/AndroidManifest.xml", NativeAndroidManifest(isLocal)),
            ("Platforms/iOS/Info.plist", NativeInfoPlist(isLocal)),
            ("Platforms/iOS/Main.cs", NativeMainCs),
            ("README.md", NativeReadme),
            ("AGENTS.md", NativeAgents),
        };

        if (isLocal)
        {
            // Native + Local: the shared component tree plus the in-process platform heads (each boots a
            // NativeAppHost + RunLocalAsync<App>) and the native geolocation backends they register.
            files.Add(("App.cs", NativeApp));
            files.Add(("HomePage.cs", NativeHomePage));
            files.Add(("Counter.cs", NativeCounter));
            files.Add(("Platforms/iOS/AppDelegate.cs", NativeIosAppDelegate));
            files.Add(("Platforms/iOS/NativeGeolocation.cs", NativeIosGeolocation));
            files.Add(("Platforms/Android/MainActivity.cs", NativeAndroidMainActivity));
            files.Add(("Platforms/Android/NativeGeolocation.cs", NativeAndroidGeolocation));
        }
        else
        {
            // Native + Server: a thin shell — just the two platform heads that point RaskServerWebView at a
            // remote Rask Server. No shared component code runs on the device.
            files.Add(("Platforms/iOS/ServerAppDelegate.cs", NativeIosServerAppDelegate));
            files.Add(("Platforms/Android/ServerActivity.cs", NativeAndroidServerActivity));
        }

        // One replacement pass: the placeholder namespace + the csproj filename become the app name.
        var scaffoldFiles = files.Select(f => new ScaffoldFile(
            System.IO.Path.Combine(targetDirectory, f.Path.Replace(NameToken, name, StringComparison.Ordinal)),
            f.Content.Replace(NameToken, name, StringComparison.Ordinal))).ToList();

        return new ScaffoldResult(scaffoldFiles, NativeNextSteps(name, host))
        {
            Packages = ["Rask.Native"],
        };
    }

    private static string NativeCsproj(string version) =>
        $"""
        <Project Sdk="Microsoft.NET.Sdk">

          <!--
            A native iOS + Android app that hosts a Rask app in-process inside a platform WebView (the WebView
            hybrid — C# runs natively, the view is a WebView). Multi-targets the two native TFMs; the shared
            component code (App.cs / HomePage.cs / Counter.cs) compiles for both, and each platform head under
            Platforms/ provides the INativeWebView implementation for its WebView control.

            Requires the iOS and/or Android SDK workloads:
                dotnet workload install ios android
            Run on a simulator/emulator:
                dotnet build -t:Run -f net10.0-android
                dotnet build -t:Run -f net10.0-ios
          -->
          <PropertyGroup>
            <TargetFrameworks>net10.0-ios;net10.0-android</TargetFrameworks>
            <OutputType>Exe</OutputType>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RootNamespace>Company.RaskServer</RootNamespace>
            <ApplicationTitle>Rask App</ApplicationTitle>
            <ApplicationId>com.example.raskapp</ApplicationId>
            <ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>
            <ApplicationVersion>1</ApplicationVersion>
          </PropertyGroup>

          <PropertyGroup Condition="$(TargetFramework.Contains('-ios'))">
            <SupportedOSPlatformVersion>15.0</SupportedOSPlatformVersion>
          </PropertyGroup>

          <!-- Wire Platforms/iOS/Info.plist as THE app manifest (a None item whose Link filename is "Info.plist"
               is how .NET iOS finds it). Its UILaunchScreen makes the app render full-screen at the device's native
               resolution — without a launch screen iOS runs it letterboxed (black bars + everything scaled up). -->
          <ItemGroup Condition="$(TargetFramework.Contains('-ios'))">
            <None Remove="Platforms/iOS/Info.plist"/>
            <None Include="Platforms/iOS/Info.plist" Link="Info.plist"/>
          </ItemGroup>

          <PropertyGroup Condition="$(TargetFramework.Contains('-android'))">
            <SupportedOSPlatformVersion>24.0</SupportedOSPlatformVersion>
            <AndroidManifest>Platforms/Android/AndroidManifest.xml</AndroidManifest>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Rask.Native" Version="{version}"/>
          </ItemGroup>

          <!-- Each platform head compiles only for its own TFM. -->
          <ItemGroup Condition="!$(TargetFramework.Contains('-ios'))">
            <Compile Remove="Platforms/iOS/**/*.cs"/>
          </ItemGroup>
          <ItemGroup Condition="!$(TargetFramework.Contains('-android'))">
            <Compile Remove="Platforms/Android/**/*.cs"/>
          </ItemGroup>

        </Project>

        """;

    // Info.plist carries an inline `<!--#if (IsLocal) -->` block (the NSLocationWhenInUseUsageDescription for
    // the native geolocation backend). Include it only for the local host; strip the marker comment lines in
    // both cases — the same resolution the template engine did for the `#if`.
    private static string NativeInfoPlist(bool isLocal)
    {
        var locationBlock = isLocal
            ? "\n\t<!-- Shown in the iOS location prompt for NativeGeolocation (the IGeolocation backend). -->"
              + "\n\t<key>NSLocationWhenInUseUsageDescription</key>"
              + "\n\t<string>Shows your current location in the app.</string>"
            : "";

        return $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
        	<key>CFBundleDisplayName</key>
        	<string>Rask App</string>
        	<key>CFBundleIdentifier</key>
        	<string>com.example.raskapp</string>
        	<key>CFBundleShortVersionString</key>
        	<string>1.0</string>
        	<key>CFBundleVersion</key>
        	<string>1</string>
        	<key>LSRequiresIPhoneOS</key>
        	<true/>
        	<key>MinimumOSVersion</key>
        	<string>15.0</string>
        	<key>UIDeviceFamily</key>
        	<array>
        		<integer>1</integer>
        		<integer>2</integer>
        	</array>
        	<!-- A launch screen is REQUIRED for iOS to render at the device's native resolution; without one the
        	     app runs letterboxed at a legacy size (black bars + everything scaled up). An empty UILaunchScreen
        	     dict opts into full-screen with a blank system launch screen (no storyboard file needed). -->
        	<key>UILaunchScreen</key>
        	<dict/>{locationBlock}

        	<key>UISupportedInterfaceOrientations</key>
        	<array>
        		<string>UIInterfaceOrientationPortrait</string>
        		<string>UIInterfaceOrientationLandscapeLeft</string>
        		<string>UIInterfaceOrientationLandscapeRight</string>
        	</array>
        </dict>
        </plist>

        """;
    }

    // AndroidManifest.xml carries the same inline `<!--#if (IsLocal) -->` block (the ACCESS_FINE_LOCATION +
    // POST_NOTIFICATIONS permissions the native backends need). Resolved per host, markers stripped.
    private static string NativeAndroidManifest(bool isLocal)
    {
        var permissionsBlock = isLocal
            ? "\n\t<!-- Required by NativeGeolocation (the IGeolocation backend). Remove if you don't use native location. -->"
              + "\n\t<uses-permission android:name=\"android.permission.ACCESS_FINE_LOCATION\"/>"
              + "\n\t<!-- Required by NativeNotifications (the INotifications backend) on API 33+. Remove if unused. -->"
              + "\n\t<uses-permission android:name=\"android.permission.POST_NOTIFICATIONS\"/>"
            : "";

        return $"""
        <?xml version="1.0" encoding="utf-8"?>
        <manifest xmlns:android="http://schemas.android.com/apk/res/android">
        	<uses-permission android:name="android.permission.INTERNET"/>{permissionsBlock}
        	<application android:label="Rask App" android:allowBackup="true" android:supportsRtl="true"/>
        </manifest>

        """;
    }

    private static string NativeNextSteps(string name, string host)
    {
        var steps = new StringBuilder();
        steps.Append("Created ").Append(name).Append(" (Rask native mobile app, ").Append(host).Append(" host).\n\nNext steps:\n");
        steps.Append("  cd ").Append(name).Append('\n');
        steps.Append("  dotnet workload install ios android   # once, if not already installed\n");
        steps.Append("  dotnet build -t:Run -f net10.0-android     # Android emulator\n");
        steps.Append("  dotnet build -t:Run -f net10.0-ios         # iOS simulator (macOS + Xcode)\n");
        return steps.ToString();
    }

    // ---- verbatim template files (namespace token Company.RaskServer replaced centrally) ----

    private const string HomePage =
        """
        using Rask.Core.Routing;

        namespace Company.RaskServer;

        [Route("/")]
        public sealed class HomePage : Component
        {
            protected override Component? Render() =>
                Div(Class: "welcome-card")[
                    H1(Class: "welcome-title")["Hello, Rask!"],
                    P(Class: "welcome-lead")["Welcome to your new app."],
                    P(Class: "welcome-hint")[
                        "This card is styled by a sibling ",
                        Code()["HomePage.css"],
                        " file — selectors are auto-scoped to this component."
                    ]
                ];
        }

        """;

    private const string HomePageCss =
        """
        .welcome-card {
            max-width: 540px;
            margin: 3rem auto;
            padding: 1.75rem 2rem;
            border: 1px solid #e1e4e8;
            border-radius: 10px;
            background: linear-gradient(180deg, #ffffff 0%, #f9fafb 100%);
            box-shadow: 0 1px 2px rgba(0, 0, 0, 0.04), 0 6px 18px rgba(0, 0, 0, 0.06);
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
        }

        .welcome-title {
            margin: 0 0 0.5rem;
            font-size: 1.75rem;
            color: #1f2937;
        }

        .welcome-lead {
            margin: 0 0 1rem;
            font-size: 1.05rem;
            color: #374151;
        }

        .welcome-hint {
            margin: 0;
            font-size: 0.9rem;
            color: #6b7280;
        }

        .welcome-hint code {
            background: #f3f4f6;
            padding: 0.1rem 0.35rem;
            border-radius: 4px;
            font-size: 0.85em;
            color: #1f2937;
        }

        """;

    private const string CounterCs =
        """
        using Rask.Core.Routing;

        namespace Company.RaskServer;

        [Route("/counter")]
        public sealed class Counter : Component
        {
            private int _count;

            protected override Component? Render() =>
                [
                    H1()["Counter"],
                    P()[$"Current count: {_count}"],
                    BsButton(Color: BsColor.Primary,
                        OnClick: () => _count++)["Click me"]
                ];
        }

        """;

    private const string WeatherCs =
        """
        using Rask.Core.Routing;

        namespace Company.RaskServer;

        [Route("/weather")]
        public sealed class Weather(IWeatherForecastService service) : Component
        {
            private WeatherForecast[]? _forecasts;

            protected override async Task OnMountAsync() =>
                _forecasts = await service.GetForecastsAsync(CancellationToken);

            protected override Component? Render() =>
                [
                    H1()["Weather"],
                    P()["This component demonstrates showing async data."],
                    _forecasts is null
                        ? P()[Em()["Loading..."]]
                        : Table()[
                            Thead()[
                                Tr()[
                                    Th()["Date"],
                                    Th()["Temp. (C)"],
                                    Th()["Temp. (F)"],
                                    Th()["Summary"]
                                ]
                            ],
                            Tbody()[_forecasts.Select(f => Tr(Key: f.Date)[
                                Td()[f.Date.ToString("yyyy-MM-dd")],
                                Td()[f.TemperatureC],
                                Td()[f.TemperatureF],
                                Td()[f.Summary ?? ""]
                            ]).ToArray()]
                        ]
                ];
        }

        """;

    private const string WeatherForecastCs =
        """
        namespace Company.RaskServer;

        public sealed record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
        {
            public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
        }

        public interface IWeatherForecastService
        {
            Task<WeatherForecast[]> GetForecastsAsync(CancellationToken cancellationToken = default);
        }

        """;

    private const string LocalWeatherServiceCs =
        """
        namespace Company.RaskServer;

        public sealed class LocalWeatherForecastService : IWeatherForecastService
        {
            private static readonly string[] Summaries =
                ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

            public async Task<WeatherForecast[]> GetForecastsAsync(CancellationToken cancellationToken = default)
            {
                await Task.Delay(500, cancellationToken);
                var startDate = DateOnly.FromDateTime(DateTime.Now);
                var rng = Random.Shared;
                return Enumerable.Range(1, 5).Select(i => new WeatherForecast(
                    startDate.AddDays(i),
                    rng.Next(-20, 55),
                    Summaries[rng.Next(Summaries.Length)]
                )).ToArray();
            }
        }

        """;

    private const string LaunchSettings =
        """
        {
          "profiles": {
            "Company.RaskServer": {
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

    private const string AuthCredentialStore =
        """
        using System.Security.Claims;

        namespace Company.RaskServer;

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

        public sealed class LoginModel
        {
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
        }

        """;

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

    private const string DockerIgnore =
        """
        # Keep the build context small and reproducible — the image restores/publishes from source.
        bin/
        obj/
        .git/
        .gitignore
        .vs/
        .vscode/
        .idea/
        *.user
        **/.DS_Store
        Dockerfile
        .dockerignore

        """;

    private const string IconSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="512" height="512">
          <defs>
            <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
              <stop offset="0" stop-color="#7C3AED"/>
              <stop offset="1" stop-color="#512BD4"/>
            </linearGradient>
          </defs>
          <!-- Maskable safe zone: keep the glyph within the central 80%. Full-bleed background. -->
          <rect width="512" height="512" fill="#faf9fe"/>
          <rect x="56" y="56" width="400" height="400" rx="88" fill="url(#g)"/>
          <path d="M300 120 L196 248 L256 248 L240 392 L356 236 L292 236 Z" fill="#ffffff"/>
        </svg>

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

    // ---- wasm-only template files (namespace token Company.RaskServer replaced centrally) ----

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
            listen 80;
            root /usr/share/nginx/html;
            index index.html;

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
        EXPOSE 80

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

        namespace Company.RaskServer;

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

        namespace Company.RaskServer;

        [Route("login")]
        [AllowAnonymous]
        public sealed class LoginPage(JwtLoginService login) : Component
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

        namespace Company.RaskServer;

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

        public sealed class MemberContent(JwtLoginService login, IUserProvider userProvider) : Component
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

    private const string WasmReadme =
        """
        # Company.RaskServer

        A standalone browser-WASM [Rask](https://github.com/pal-tamas/rask) SPA (`net10.0-browser`).
        It runs entirely in the browser using the `JSImport`/`JSExport` transport — there is no
        ASP.NET host of its own.

        > Scaffolded with `rask new --template wasm` — Rask is the .NET One Person Framework.

        ## Run

        ```bash
        rask dev        # hot reload (or: dotnet run)
        ```

        ## Publish

        ```bash
        dotnet publish -c Release
        ```

        Publishing trims the app and bakes scoped CSS/JS assets into the bundle; serve the output
        from any static-file host. Keep the publish IL-trim-clean — new reflection needs a
        `[DynamicallyAccessedMembers]` annotation or a justified suppression.

        ## Layout

        - `Program.cs` — `WasmHostBuilder.CreateDefault()` + `RunAsync<App>()`.
        - `App.cs` — the root component (renders the full shell).
        - `HomePage.cs` (+ `HomePage.css`), `Counter.cs`, `Weather.cs` — pages and components.

        Add a full CRUD feature in one command: `rask generate feature Product Name:string Price:decimal`.

        Next steps: the [Rask docs](https://github.com/pal-tamas/rask/tree/main/docs).

        """;

    private const string WasmAgents =
        """
        # AGENTS.md — building this app with an AI assistant

        This is a **Rask** app. Rask is the .NET One Person Framework (a full-stack C# framework for .NET 10). This
        file tells AI coding assistants the conventions so generated code compiles and runs. Full docs:
        https://github.com/pal-tamas/rask/tree/main/docs

        ## Mental model
        - Components are **plain C# classes** deriving from `Component`. Override `Component? Render()`
          and return a tree of HTML built with **generated factory methods** — no `.razor`, no JSX.
        - This is a standalone browser-WASM SPA (`net10.0-browser`) — the same component model also runs
          server-rendered; here it runs entirely in the browser, so services talk to external HTTP APIs.

        ## The rules that matter
        - **Use factories, never `new`** for components: `Div(...)`, `Button(OnClick: ...)`. `new` outside the
          framework is a compile error (RASK014).
        - **Children go through the indexer**, not a constructor arg: `Div()[Span()["hi"], "text"]`. A bare
          `string` becomes a text node; pass a list directly for collections: `Ul()[items]`. `..` spread does not work.
        - **Props are factory parameters.** A nullable prop is optional; a non-nullable prop with no initializer is
          **required**. Inject services (`HttpClient`, `Navigator`, `IJSRuntime`, typed browser APIs) through the
          **constructor**, not settable properties.
        - **A page/root component renders the full shell**: `[Doctype(), Html(...)[Head(...), Body(...)]]` (RASK021).
        - **Text vs raw:** a bare string / `Text("..")` HTML-encodes; `Raw("..")` is verbatim (XSS risk).
        - **Keep it trim-clean:** the WASM publish trims; new reflection needs `[DynamicallyAccessedMembers]`.
        - Route with `[Route("/users/{id:int}")]` + `[RouteParam]`/`[QueryParam]`. Navigate from event handlers via
          injected `Navigator`; prefer the generated `Routes.*()` URL helpers over string paths (RASK033).

        ## Scaffolding — use the `rask` CLI
        - `rask generate page <Name>` / `rask generate component <Name>` scaffold a routed page / a component.
        - `rask generate feature <Name> <field:type> …` emits a full CQRS + EF Core CRUD vertical slice. Add
          `Rask.Cqrs` (reflection-free, trims clean) for a mediator. See docs/cli.md.
        - `rask dev` runs the app with hot reload; `rask new` scaffolds a project.

        If you hit a `RASKxxx` compile error, see https://github.com/pal-tamas/rask/blob/main/docs/diagnostics.md

        """;

    // ---- native-only template files (namespace token Company.RaskServer replaced centrally) ----

    private const string NativeApp =
        """
        using static Company.RaskServer.Routes;
        using NativeIcon = Rask.Native.Components.NativeIcon;

        // NativeHeaderBar / NativeTabBar / NativeTab / NativeWebView factories come from a global using the generator
        // emits automatically for any project referencing Rask.Native — no `using static` needed here.

        namespace Company.RaskServer;

        // The root component. A native page is a small COMPOSED tree: the native bars (NativeHeaderBar / NativeTabBar)
        // as siblings of a NativeWebView, which hosts the ordinary page shell (Doctype/Html/Head/Body, RASK021). The
        // native host projects the bars to REAL platform chrome — a UINavigationBar + UITabBar on iOS, a top bar +
        // bottom tab bar on Android — and serializes the NativeWebView's HTML into the WebView between them.
        public sealed class App : Component
        {
            protected override Component? Head =>
            [
                Title()["Rask App"],
                Meta("utf-8"),
                Meta(Name: "viewport", Content: "width=device-width, initial-scale=1, viewport-fit=cover")
            ];

            protected override Component? Render() =>
            [
                // Real native top bar. Opt in by hosting webView.ChromeView + registering the head as INativeChrome —
                // see Platforms/iOS/AppDelegate.cs and Platforms/Android/MainActivity.cs.
                NativeHeaderBar(Title: "Rask App"),

                // The HTML surface — its children are the normal page shell, morphed into the platform WebView.
                NativeWebView()[
                    Doctype(),
                    Html("en")[
                        Head(),
                        // Pad the body by the device safe-area insets so content clears the status bar / notch /
                        // home indicator (the boot shell requests an edge-to-edge viewport with viewport-fit=cover).
                        Body(Style: "margin:0;padding:env(safe-area-inset-top) env(safe-area-inset-right) " +
                                    "env(safe-area-inset-bottom) env(safe-area-inset-left)")[
                            Router()
                        ]
                    ]
                ],

                // Real native bottom tab bar — primary navigation. Tapping a tab routes to its type-safe To:.
                NativeTabBar(
                    Tabs:
                    [
                        NativeTab(Title: "Home", Icon: NativeIcon.Home, To: HomePage()),
                        NativeTab(Title: "Counter", Icon: NativeIcon.Add, To: Counter())
                    ])
                // Selected is omitted — the framework highlights the tab matching the current route.
            ];
        }

        """;

    private const string NativeHomePage =
        """
        using Rask.Core.Routing;

        namespace Company.RaskServer;

        [Route("/")]
        public sealed class HomePage : Component
        {
            protected override Component? Render() =>
                Div()[
                    H1()["Hello, Rask — natively!"],
                    P()["This is a native iOS/Android app. The same C# component code runs here as on the "
                        + "server and in the browser — it's just packaged for the App Store / Play Store."],
                    P()["Open Counter to see live, in-process state updates over the native WebView bridge."]
                ];
        }

        """;

    private const string NativeCounter =
        """
        using Rask.Core.Routing;

        namespace Company.RaskServer;

        [Route("/counter")]
        public sealed class Counter : Component
        {
            private int _count;

            protected override Component? Render() =>
            [
                H1()["Counter"],
                P()[$"Current count: {_count}"],
                Button(OnClick: () => _count++)["Click me"]
            ];
        }

        """;

    private const string NativeMainCs =
        """
        using UIKit;

        namespace Company.RaskServer;

        // iOS entry point. Hands control to UIKit with our AppDelegate.
        public static class Program
        {
            private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
        }

        """;

    private const string NativeIosAppDelegate =
        """
        using Foundation;
        using Microsoft.Extensions.DependencyInjection;
        using Rask.Client.Browser;
        using Rask.Core.Browser;
        using Rask.Native;
        using UIKit;

        namespace Company.RaskServer;

        [Register("AppDelegate")]
        public class AppDelegate : UIApplicationDelegate
        {
            public override UIWindow? Window { get; set; }
            private NativeApp? _app;

            public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
            {
                Window = new UIWindow(UIScreen.MainScreen.Bounds);

                var webView = new RaskWkWebView();
                // Host ChromeView (the container that lays the native header/footer bars around the WebView) rather
                // than the bare WebView, so the App's composed NativeHeaderBar/NativeTabBar become a real
                // UINavigationBar/UITabBar. The INativeChrome backend is registered below; without it the bars render
                // nothing (the WebView fills the screen), so a native app that navigates via the tab bar should keep it.
                Window.RootViewController = new UIViewController { View = webView.ChromeView };
                Window.MakeKeyAndVisible();

                // Wire the in-process session BEFORE loading the shell, so the session is ready to receive the
                // client's `ready` handshake and push the first frame. Native + Local mode.
                _ = StartAsync(webView);
                return true;
            }

            private async Task StartAsync(RaskWkWebView webView)
            {
                var host = NativeAppHost.CreateDefault();
                // Native device backends: override Rask.Native's JS-backed defaults with the platform APIs. Register
                // any native backend on host.Services before RunLocalAsync — the last registration wins. See
                // docs/native.md "Native device backends".
                host.Services.AddSingleton<IShare>(_ => new NativeShare(() => Window?.RootViewController));  // share sheet
                host.Services.AddSingleton<IGeolocation>(_ => new NativeGeolocation());                     // CoreLocation
                host.Services.AddSingleton<INotifications>(_ => new NativeNotifications());                 // UNUserNotificationCenter
                host.Services.AddSingleton<IBadge>(_ => new NativeBadge());                                 // app-icon badge
                host.Services.AddSingleton<INativeChrome>(webView);                                         // native header/footer bars
                // host.Services.AddSingleton<IMyService, MyService>();   // register app services here
                _app = await host.RunLocalAsync<App>(webView);
                webView.LoadShell();
            }

            public override void WillTerminate(UIApplication application) => _ = _app?.DisposeAsync();
        }

        """;

    private const string NativeIosServerAppDelegate =
        """
        using Foundation;
        using Rask.Native;
        using UIKit;

        namespace Company.RaskServer;

        // Native + Server mode (iOS): a thin native shell over a REMOTE Rask Server. The WebView machinery — the
        // native capability bridge (so the remote page's Shareable / IShare reach the device's native backends),
        // the trusted-origin gating, and the off-origin-to-Safari diversion — lives in Rask.Native's
        // RaskServerViewController. This head just points it at your server and supplies the native share backend.
        [Register("AppDelegate")]
        public class AppDelegate : UIApplicationDelegate
        {
            // Your remote Rask Server (a real deployment is https).
            private static readonly Uri ServerOrigin = new("https://app.example.com/");

            public override UIWindow? Window { get; set; }

            public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
            {
                var shell = NativeAppHost.ConnectToServer(ServerOrigin);
                Window = new UIWindow(UIScreen.MainScreen.Bounds)
                {
                    RootViewController = new RaskServerViewController(
                        shell.ServerBaseUrl, new NativeShare(() => Window?.RootViewController))
                };
                Window.MakeKeyAndVisible();
                return true;
            }
        }

        """;

    private const string NativeIosGeolocation =
        """
        using CoreLocation;
        using Foundation;
        using Rask.Core.Browser;
        using UIKit;

        namespace Company.RaskServer;

        // Native iOS backend for IGeolocation — CoreLocation instead of the WebView's navigator.geolocation.
        // Registered in AppDelegate before RunLocalAsync (overrides Rask's JS-backed default). Native location
        // gives the real iOS permission prompt (Info.plist NSLocationWhenInUseUsageDescription) and CLLocationManager
        // accuracy, and works even where the WebView's geolocation is restricted.
        //
        // This is the same framework-default → native-head-override pattern as NativeShare, for a request/response
        // (+ subscription) capability: GetCurrentPositionAsync resolves one fix; WatchAsync streams them.
        public sealed class NativeGeolocation : IGeolocation
        {
            public ValueTask<GeolocationPosition> GetCurrentPositionAsync(GeolocationOptions? options = null)
            {
                var tcs = new TaskCompletionSource<GeolocationPosition>();
                UIApplication.SharedApplication.InvokeOnMainThread(() =>
                {
                    // The delegate keeps the manager alive until it fires (or faults), then tears it down.
                    var manager = new CLLocationManager
                    {
                        DesiredAccuracy = (options?.EnableHighAccuracy ?? false)
                            ? CLLocation.AccuracyBest
                            : CLLocation.AccuracyHundredMeters
                    };
                    manager.Delegate = new OneShotDelegate(manager, tcs);
                    manager.RequestWhenInUseAuthorization();
                    RequestWhenAuthorized(manager);
                });
                return new ValueTask<GeolocationPosition>(tcs.Task);
            }

            public ValueTask<IAsyncDisposable> WatchAsync(
                Func<GeolocationPosition, Task> onPosition, GeolocationOptions? options = null)
            {
                var watch = new Watch(onPosition, options?.EnableHighAccuracy ?? false);
                return new ValueTask<IAsyncDisposable>(watch);
            }

            // RequestLocation only works once authorization is at least WhenInUse; if it's still NotDetermined the
            // delegate re-requests from DidChangeAuthorization once the user answers the prompt.
            private static void RequestWhenAuthorized(CLLocationManager manager)
            {
                if (manager.AuthorizationStatus is CLAuthorizationStatus.AuthorizedWhenInUse
                    or CLAuthorizationStatus.AuthorizedAlways)
                {
                    manager.RequestLocation();
                }
            }

            private sealed class OneShotDelegate(CLLocationManager manager, TaskCompletionSource<GeolocationPosition> tcs)
                : CLLocationManagerDelegate
            {
                public override void AuthorizationChanged(CLLocationManager mgr, CLAuthorizationStatus status)
                {
                    if (status is CLAuthorizationStatus.AuthorizedWhenInUse or CLAuthorizationStatus.AuthorizedAlways)
                    {
                        mgr.RequestLocation();
                    }
                    else if (status is CLAuthorizationStatus.Denied or CLAuthorizationStatus.Restricted)
                    {
                        Finish(() => tcs.TrySetException(new InvalidOperationException("Location permission denied.")));
                    }
                }

                public override void LocationsUpdated(CLLocationManager mgr, CLLocation[] locations)
                {
                    if (locations.Length > 0)
                    {
                        Finish(() => tcs.TrySetResult(Map(locations[^1])));
                    }
                }

                public override void Failed(CLLocationManager mgr, NSError error) =>
                    Finish(() => tcs.TrySetException(new InvalidOperationException(error.LocalizedDescription)));

                private void Finish(Action complete)
                {
                    manager.Delegate = null;   // release the retain cycle so the manager can be collected
                    complete();
                }
            }

            // A watchPosition subscription: streams every CLLocation update until disposed.
            private sealed class Watch : CLLocationManagerDelegate, IAsyncDisposable
            {
                private readonly Func<GeolocationPosition, Task> _onPosition;
                private CLLocationManager? _manager;

                public Watch(Func<GeolocationPosition, Task> onPosition, bool highAccuracy)
                {
                    _onPosition = onPosition;
                    UIApplication.SharedApplication.InvokeOnMainThread(() =>
                    {
                        _manager = new CLLocationManager
                        {
                            DesiredAccuracy = highAccuracy ? CLLocation.AccuracyBest : CLLocation.AccuracyHundredMeters,
                            Delegate = this
                        };
                        _manager.RequestWhenInUseAuthorization();
                        if (_manager.AuthorizationStatus is CLAuthorizationStatus.AuthorizedWhenInUse
                            or CLAuthorizationStatus.AuthorizedAlways)
                        {
                            _manager.StartUpdatingLocation();
                        }
                    });
                }

                public override void AuthorizationChanged(CLLocationManager mgr, CLAuthorizationStatus status)
                {
                    if (status is CLAuthorizationStatus.AuthorizedWhenInUse or CLAuthorizationStatus.AuthorizedAlways)
                    {
                        mgr.StartUpdatingLocation();
                    }
                }

                public override void LocationsUpdated(CLLocationManager mgr, CLLocation[] locations)
                {
                    if (locations.Length > 0)
                    {
                        _ = _onPosition(Map(locations[^1]));
                    }
                }

                public ValueTask DisposeAsync()
                {
                    UIApplication.SharedApplication.InvokeOnMainThread(() =>
                    {
                        _manager?.StopUpdatingLocation();
                        if (_manager is not null)
                        {
                            _manager.Delegate = null;
                        }
                    });
                    return default;
                }
            }

            private static GeolocationPosition Map(CLLocation l) => new(
                Latitude: l.Coordinate.Latitude,
                Longitude: l.Coordinate.Longitude,
                Accuracy: l.HorizontalAccuracy,
                Altitude: l.VerticalAccuracy > 0 ? l.EllipsoidalAltitude : null,
                AltitudeAccuracy: l.VerticalAccuracy > 0 ? l.VerticalAccuracy : null,
                Heading: l.Course >= 0 ? l.Course : null,
                Speed: l.Speed >= 0 ? l.Speed : null,
                TimestampMs: (l.Timestamp.SecondsSince1970) * 1000.0);
        }

        """;

    private const string NativeAndroidMainActivity =
        """
        using Android.App;
        using Android.Content.PM;
        using Android.OS;
        using Microsoft.Extensions.DependencyInjection;
        using Rask.Client.Browser;
        using Rask.Core.Browser;
        using Rask.Native;

        namespace Company.RaskServer;

        [Activity(Label = "Rask App", MainLauncher = true, Exported = true,
            Theme = "@android:style/Theme.Material.Light.NoActionBar")]
        public class MainActivity : Activity
        {
            private NativeApp? _app;
            private RaskAndroidWebView? _webView;

            protected override void OnCreate(Bundle? savedInstanceState)
            {
                base.OnCreate(savedInstanceState);

                // NativeGeolocation (registered below) needs the runtime location grant — request it up front so a
                // later GetCurrentPositionAsync finds it granted (declare ACCESS_FINE_LOCATION in AndroidManifest.xml).
                if (CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) != Permission.Granted)
                {
                    RequestPermissions([Android.Manifest.Permission.AccessFineLocation], 100);
                }

                // NativeNotifications (registered below) needs the POST_NOTIFICATIONS runtime grant on API 33+ —
                // request it up front so a later ShowAsync posts (declare POST_NOTIFICATIONS in AndroidManifest.xml).
                if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
                    CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted)
                {
                    RequestPermissions([Android.Manifest.Permission.PostNotifications], 101);
                }

                _webView = new RaskAndroidWebView(this);
                // Host ChromeView (the container that lays the native header/footer bars around the WebView) rather
                // than the bare WebView, so the App's composed NativeHeaderBar/NativeTabBar become real top/bottom bars.
                // The INativeChrome backend is registered below; without it the bars render nothing (the WebView fills
                // the screen), so a native app that navigates via the tab bar should keep that registration.
                SetContentView(_webView.ChromeView);

                // Wire the in-process session before loading the shell so it's ready for the client's `ready`
                // handshake. Native + Local mode.
                _ = StartAsync(_webView);
            }

            private async Task StartAsync(RaskAndroidWebView webView)
            {
                var host = NativeAppHost.CreateDefault();
                // Native device backends: override Rask.Native's JS-backed defaults with the platform APIs. Register
                // any native backend on host.Services before RunLocalAsync — the last registration wins. See
                // docs/native.md "Native device backends".
                host.Services.AddSingleton<IShare>(_ => new NativeShare(this));                  // OS share sheet
                host.Services.AddSingleton<IGeolocation>(_ => new NativeGeolocation(this));       // LocationManager
                host.Services.AddSingleton<INotifications>(_ => new NativeNotifications(this));   // NotificationManager
                host.Services.AddSingleton<IBadge>(_ => new NativeBadge(this));                   // app badge notification
                host.Services.AddSingleton<INativeChrome>(webView);                              // native header/footer bars
                // host.Services.AddSingleton<IMyService, MyService>();   // register app services here
                _app = await host.RunLocalAsync<App>(webView);
                webView.LoadShell();
            }

            // Forward runtime-permission results so NativeNotifications' RequestPermissionAsync can await the grant.
            public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
            {
                NativePermissions.OnResult(requestCode, grantResults);
                base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            }

            protected override void OnDestroy()
            {
                _ = _app?.DisposeAsync();
                base.OnDestroy();
            }
        }

        """;

    private const string NativeAndroidServerActivity =
        """
        using Android.App;
        using Android.OS;
        using Rask.Native;

        namespace Company.RaskServer;

        // Native + Server mode: a thin native shell over a REMOTE Rask Server. The WebView machinery — the native
        // capability bridge (so the remote page's Shareable / IShare reach the device's native backends), the
        // trusted-origin gating, and the off-origin-to-system-browser diversion — lives in Rask.Native's
        // RaskServerWebView. This head just points it at your server and supplies the native share backend.
        [Activity(Label = "Rask App", MainLauncher = true, Exported = true,
            Theme = "@android:style/Theme.Material.Light.NoActionBar")]
        public class ServerActivity : Activity
        {
            // Your remote Rask Server. (Android emulator → host machine is http://10.0.2.2:<port>; a real
            // deployment is https. For http during development, allow cleartext in AndroidManifest.xml.)
            private static readonly Uri ServerOrigin = new("https://app.example.com/");

            protected override void OnCreate(Bundle? savedInstanceState)
            {
                base.OnCreate(savedInstanceState);
                SetContentView(RaskServerWebView.Create(this, ServerOrigin, new NativeShare(this)));
            }
        }

        """;

    private const string NativeAndroidGeolocation =
        """
        using Android.App;
        using Android.Content;
        using Android.Content.PM;
        using Android.Locations;
        using Android.OS;
        using Android.Runtime;
        using Rask.Core.Browser;

        namespace Company.RaskServer;

        // Native Android backend for IGeolocation — the platform LocationManager instead of the WebView's
        // navigator.geolocation. Registered in MainActivity before RunLocalAsync (overrides Rask's JS-backed
        // default). Needs ACCESS_FINE_LOCATION in AndroidManifest.xml + a runtime grant (MainActivity requests it
        // on launch). Same framework-default → native-head-override pattern as NativeShare, for a request/response
        // (+ subscription) capability.
        public sealed class NativeGeolocation(Activity activity) : IGeolocation
        {
            public ValueTask<GeolocationPosition> GetCurrentPositionAsync(GeolocationOptions? options = null)
            {
                var tcs = new TaskCompletionSource<GeolocationPosition>();
                activity.RunOnUiThread(() =>
                {
                    if (!HasPermission())
                    {
                        tcs.TrySetException(new InvalidOperationException("Location permission not granted."));
                        return;
                    }

                    var manager = LocationManagerFor();
                    var provider = BestProvider(manager, options?.EnableHighAccuracy ?? false);
                    if (provider is null)
                    {
                        tcs.TrySetException(new InvalidOperationException("No enabled location provider."));
                        return;
                    }

                    // One-shot: subscribe, take the first fix, then unsubscribe.
                    OneShotListener? listener = null;
                    listener = new OneShotListener(fix =>
                    {
                        manager.RemoveUpdates(listener!);
                        tcs.TrySetResult(fix);
                    });
                    manager.RequestLocationUpdates(provider, 0L, 0f, listener, Looper.MainLooper);
                });
                return new ValueTask<GeolocationPosition>(tcs.Task);
            }

            public ValueTask<IAsyncDisposable> WatchAsync(
                Func<GeolocationPosition, Task> onPosition, GeolocationOptions? options = null)
            {
                var watch = new Watch(activity, HasPermission, onPosition, options?.EnableHighAccuracy ?? false);
                return new ValueTask<IAsyncDisposable>(watch);
            }

            private bool HasPermission() =>
                activity.CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) == Permission.Granted;

            private LocationManager LocationManagerFor() =>
                (LocationManager)activity.GetSystemService(Context.LocationService)!;

            private static string? BestProvider(LocationManager manager, bool highAccuracy)
            {
                // Prefer GPS for high accuracy; otherwise take whatever's enabled (network is faster/cheaper).
                var order = highAccuracy
                    ? new[] { LocationManager.GpsProvider, LocationManager.NetworkProvider }
                    : new[] { LocationManager.NetworkProvider, LocationManager.GpsProvider };
                foreach (var p in order)
                {
                    if (manager.IsProviderEnabled(p))
                    {
                        return p;
                    }
                }

                return null;
            }

            private static GeolocationPosition Map(Location l) => new(
                Latitude: l.Latitude,
                Longitude: l.Longitude,
                Accuracy: l.HasAccuracy ? l.Accuracy : 0,
                Altitude: l.HasAltitude ? l.Altitude : null,
                AltitudeAccuracy: l is { HasVerticalAccuracy: true } ? l.VerticalAccuracyMeters : null,
                Heading: l.HasBearing ? l.Bearing : null,
                Speed: l.HasSpeed ? l.Speed : null,
                TimestampMs: l.Time);

            private sealed class OneShotListener(Action<GeolocationPosition> onFix) : Java.Lang.Object, ILocationListener
            {
                public void OnLocationChanged(Location location) => onFix(Map(location));

                public void OnProviderDisabled(string provider) { }

                public void OnProviderEnabled(string provider) { }

                public void OnStatusChanged(string? provider, [GeneratedEnum] Availability status, Bundle? extras) { }
            }

            private sealed class Watch : Java.Lang.Object, ILocationListener, IAsyncDisposable
            {
                private readonly Activity _activity;
                private readonly Func<GeolocationPosition, Task> _onPosition;
                private LocationManager? _manager;

                public Watch(Activity activity, Func<bool> hasPermission, Func<GeolocationPosition, Task> onPosition, bool highAccuracy)
                {
                    _activity = activity;
                    _onPosition = onPosition;
                    activity.RunOnUiThread(() =>
                    {
                        if (!hasPermission())
                        {
                            return;
                        }

                        _manager = (LocationManager)activity.GetSystemService(Context.LocationService)!;
                        var provider = BestProvider(_manager, highAccuracy);
                        if (provider is not null)
                        {
                            _manager.RequestLocationUpdates(provider, 1000L, 0f, this, Looper.MainLooper);
                        }
                    });
                }

                public void OnLocationChanged(Location location) => _ = _onPosition(Map(location));

                public void OnProviderDisabled(string provider) { }

                public void OnProviderEnabled(string provider) { }

                public void OnStatusChanged(string? provider, [GeneratedEnum] Availability status, Bundle? extras) { }

                public ValueTask DisposeAsync()
                {
                    _activity.RunOnUiThread(() => _manager?.RemoveUpdates(this));
                    return default;
                }
            }
        }

        """;

    private const string NativeReadme =
        """
        # Company.RaskServer

        A **native iOS + Android** app built with [Rask](https://github.com/pal-tamas/rask). The same C#
        component code that runs on the Rask server and in the browser runs here too — packaged as a real,
        store-distributable mobile app. It's a **WebView hybrid**: your C# runs natively on the device, and the
        UI renders in a platform WebView driven by Rask's live diff pipeline.

        ## Prerequisites

        Install the iOS and/or Android SDK workloads:

        ```bash
        dotnet workload install ios android
        ```

        ## Run

        ```bash
        dotnet build -t:Run -f net10.0-android     # Android emulator
        dotnet build -t:Run -f net10.0-ios         # iOS simulator (macOS + Xcode)
        ```

        ## What's here

        - `App.cs`, `HomePage.cs`, `Counter.cs` — your Rask components (shared across both platforms). `App.cs`
          pads `Body` by `env(safe-area-inset-*)` (with a `viewport-fit=cover` viewport) so content clears the
          notch / status bar / home indicator — keep both if you restructure it.
        - `Platforms/iOS/` — the iOS head: `AppDelegate` boots a `NativeAppHost`, and `RaskWkWebView` implements
          `INativeWebView` over a `WKWebView` (custom `raskapp://` scheme + script-message bridge).
        - `Platforms/Android/` — the Android head: `MainActivity` boots the host, and `RaskAndroidWebView`
          implements `INativeWebView` over an `android.webkit.WebView` (asset-serving `WebViewClient` + a
          `@JavascriptInterface` bridge).

        Register app services on `host.Services` in the platform head's `StartAsync`, then add pages as
        `[Route("/…")]` components — exactly as in any other Rask app.

        ## Native + Server mode

        To make the app a thin native shell over a remote Rask Server instead of running in-process, point the
        WebView at the server URL:

        ```csharp
        var shell = NativeAppHost.ConnectToServer(new Uri("https://app.example.com/"));
        // iOS:     webView.View.LoadRequest(new NSUrlRequest(new NSUrl(shell.ServerBaseUrl.ToString())));
        // Android: webView.View... LoadUrl(shell.ServerBaseUrl.ToString());
        ```

        The server serves its own client and connects back over `wss://`; native device APIs remain available to
        the page.

        See the framework docs: [Native mobile apps with Rask](https://github.com/pal-tamas/rask/blob/main/docs/native.md).

        """;

    private const string NativeAgents =
        """
        # AGENTS.md — building this app with an AI assistant

        This is a **Rask** native mobile app (iOS + Android) for .NET 10. Rask is the .NET One Person Framework —
        one C# codebase, one server, every UI surface. This app is a **WebView hybrid**: your C#
        runs natively on the device via `Rask.Native`, and the UI renders in a platform WebView driven by Rask's
        live diff pipeline. The **same component code** as any other Rask host works here. Full docs:
        https://github.com/pal-tamas/rask/tree/main/docs — native specifics: docs/native.md.

        ## Mental model
        - Components are **plain C# classes** deriving from `Component`. Override `Component? Render()` and return
          a tree built with **generated factory methods** — no `.razor`, no JSX. Use factories, never `new`
          (RASK014). Children go through the indexer: `Div()[Span()["hi"], "text"]`. Props are factory params
          (nullable ⇒ optional, non-nullable no-initializer ⇒ required).
        - A page/root component must render the **full shell** `[Doctype(), Html(...)[Head(...), Body(...)]]`
          (RASK021). The framework injects its runtime automatically.
        - Route with `[Route("/path")]`; navigate only from event handlers via the injected `Navigator`. Inject
          services (`HttpClient`, `IJSRuntime`, the typed `Rask.Core.Browser` APIs, your own) through the ctor.

        ## Native structure — don't restructure these
        - **Shared components** (`App.cs`, `HomePage.cs`, `Counter.cs`) compile for both `net10.0-ios` and
          `net10.0-android`. Keep platform-specific types OUT of them. `App.cs` pads `Body` by
          `env(safe-area-inset-*)` (paired with the `viewport-fit=cover` viewport meta) so content clears the
          notch / status bar / home indicator — keep both together if you edit the shell. `App.cs` also declares the
          native header/footer bars (see "Native header & footer bars" below).
        - **Platform heads** live under `Platforms/iOS/` and `Platforms/Android/`. Each boots a
          `NativeAppHost`, calls `host.RunLocalAsync<App>(webView)`, and provides the `INativeWebView`
          implementation for its WebView (`WKWebView` on iOS, `android.webkit.WebView` on Android). Register app
          services on `host.Services` in the head's `StartAsync` before `RunLocalAsync`.
        - **Two modes:** `RunLocalAsync<App>(webView)` runs the app in-process (offline). To be a shell over a
          remote Rask Server instead, `NativeAppHost.ConnectToServer(uri)` and load that URL in the WebView.

        ## Device APIs
        - Inject the typed `Rask.Core.Browser` wrappers (`IGeolocation`, `IClipboard`, `IVibration`,
          `IBrowserStorage`, `INotifications`, `IBadge`, `IWakeLock`, …) — they work through the WebView's JS engine.
        - Sharing: use the headless `Shareable` (`Rask.Core`) to attach share behaviour to your own element, or
          inject `IShare` (`Rask.Client.Browser`) to share from code. Both hit the OS share sheet.
        - **Native backends** override a JS default with real platform code. The head registers one on
          `host.Services` **before `RunLocalAsync`** (last-wins). The template ships `NativeShare` for `IShare`
          (iOS `UIActivityViewController`, Android `Intent.ACTION_SEND`), `NativeGeolocation` for `IGeolocation`
          (iOS `CLLocationManager`, Android `LocationManager`), and `NativeNotifications` / `NativeBadge` for
          `INotifications` / `IBadge` (iOS `UNUserNotificationCenter`, Android `NotificationManager` + a badge
          notification) under `Platforms/`; register your own the same way. Geolocation needs the location
          permission and notifications need `POST_NOTIFICATIONS` on Android 33+ (both already in
          `AndroidManifest.xml` / `Info.plist`; `MainActivity` requests the runtime grants). Further native
          backends (biometrics, push) are a framework work-in-progress.

        ## Native header & footer bars
        - A native page is a small **composed tree**: the native bars (`NativeHeaderBar` / `NativeTabBar` /
          `NativeToolbar`) as siblings of a **`NativeWebView`**, which hosts the ordinary page shell
          (`Doctype`/`Html`/`Head`/`Body`). `App.cs` shows the shape. The bars are ordinary factory-built components —
          compose them in `Render()`, they are not magic base-class slots.
        - The native host projects the bars to **real platform bars** — a `UINavigationBar` + `UITabBar` on iOS, a top
          bar + bottom tab bar on Android — and serializes the `NativeWebView`'s HTML into the WebView between them.
          Build bars from `NativeBarButton` / `NativeTab` / `NativeBackButton` and type-safe `NativeIcon`s. A
          `NativeTab` also takes an optional `Badge` string (unread count) → `UITabBarItem.BadgeValue` / icon overlay.
          `NativeHeaderBar` takes optional `Segments` (shown in place of the title) → a `UISegmentedControl` / button
          row, controlled via `SelectedSegment` + `OnSegmentChanged(int)`. A `NativeMenuButton` bar item (with
          `NativeMenuItem` entries) opens a native overflow pull-down → `UIMenu` (iOS) / `PopupMenu` (Android). A
          `NativeBackButton` (header `Leading`) pops WebView history like hardware Back.
        - **Style the bars** with `NativeColor` (the color sibling of `NativeIcon`: `Hex` / `Rgba` / `Adaptive(light,
          dark)` / `System`) — set `Background` / `Tint` / `TitleColor` per bar (`NativeTabBar` also `UnselectedTint`),
          or register an app-wide `NativeTheme` on `host.Services`. Per-bar wins, then the theme, then the platform
          default; an unset color keeps the OS look.
        - **Opt-in wiring (already done in the heads):** host `webView.ChromeView` (not `webView.View`) and register
          the WebView head as `INativeChrome` on `host.Services` before `RunLocalAsync`. With no `INativeChrome`
          registered the bars are inert (they render nothing; the WebView fills the screen) — fully backward compatible.
        - Tabs navigate their type-safe `To:` route; bar buttons run their `OnClick`. Put a native chrome component
          **inside** the HTML (an element child, or inside `NativeWebView`'s content) and you get **RASK032** — bars
          belong at the layout level, as siblings of `NativeWebView`.
        - Sharing an app across web + native? Branch with `IsNative` / `IsServer` / `IsWasm` / `IsIOS` / `IsAndroid`
          (or `HostShell` / `HostEngine` / `HostPlatform`): compose the native tree under `IsNative`, return the plain
          shell otherwise.

        ## Build & run (needs the iOS/Android SDK workloads)
        ```bash
        dotnet workload install ios android
        dotnet build -t:Run -f net10.0-android     # emulator
        dotnet build -t:Run -f net10.0-ios         # simulator (macOS + Xcode)
        ```

        If you hit a `RASKxxx` compile error, see https://github.com/pal-tamas/rask/blob/main/docs/diagnostics.md

        """;
}
