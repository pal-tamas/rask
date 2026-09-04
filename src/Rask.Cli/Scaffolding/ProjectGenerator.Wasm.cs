using System.Text;

namespace Rask.Cli.Scaffolding;

// The wasm template: a standalone browser-WASM SPA.
internal static partial class ProjectGenerator
{
    /// <summary>Generates the <c>wasm</c> template (a standalone browser-WASM SPA) into <paramref name="targetDirectory"/>.</summary>
    public static ScaffoldResult GenerateWasm(string targetDirectory, string name, bool pwa,
        bool docker, string version, ServerBatteries? batteries = null)
    {
        // Both read off the batteries rather than taken as parameters beside them. Styling is one axis
        // with three answers; a bool alongside a ServerBatteries that already carries Styling is two
        // sources for one decision, and the caller that set only one of them is the bug. Localization is
        // the same argument plus a list: until #846 this parameter was accepted and never looked at, so
        // the template took --culture and scaffolded nothing.
        string[] cultures = batteries?.Localization == true ? [.. batteries.Cultures] : [];

        var files = new List<(string Path, string Content)>
        {
            ($"{NameToken}.csproj", WasmCsproj(version, cultures.Length > 0)),
            ("Program.cs", WasmProgram(pwa, cultures)),
            // The shell + welcome page are identical to the server template's (Features/Shared + Features/Home).
            ("Features/Shared/App.cs", AppShellCs()),
            ("Features/Home/HomePage.cs", HomePageTailwindCs),
            ("wwwroot/index.html", WasmIndexHtml(pwa)),
            ("runtimeconfig.template.json", WasmRuntimeConfig),
            ("tsconfig.json", TsConfigJson),
        };

        if (pwa)
        {
            files.Add(("wwwroot/icon.svg", IconSvg));
        }

        // Same input sheet as every other host: Rask.Tailwind compiles it to wwwroot/css/app.css
        // before the app builds, and the WebAssembly SDK publishes wwwroot as it finds it.
        files.Add(("Styles/app.css", TailwindInputCss));

        files.AddRange(StringCatalogs(cultures));

        if (docker)
        {
            files.Add(("Dockerfile", WasmDockerfile));
            files.Add((".dockerignore", DockerIgnore));
            files.Add(("nginx.conf", WasmNginxConf));
        }

        files.AddRange(ProjectHygiene($"{NameToken}.csproj"));

        var scaffoldFiles = Materialize(targetDirectory, name, files);

        return new ScaffoldResult(scaffoldFiles, WasmNextSteps(name, docker, cultures.Length > 0))
        {
            Packages = ["Rask.Wasm"],
        };
    }

    /// <summary>
    /// The WebAssembly SDK <c>&lt;PropertyGroup&gt;</c> for the standalone <c>wasm</c> template, with the
    /// one globalization line following whether the app names a language.
    ///
    /// <para>
    /// Factored out when a second csproj builder shared it — <c>wasm-hosted</c>'s client, removed in
    /// #877 — and kept because the shape is still worth naming once.
    /// </para>
    /// </summary>
    internal static string WasmSdkPropertyGroup(bool localization) =>
        WasmSdkPropertyGroupTemplate.Replace("@@GLOBALIZATION@@", localization
            ? GlobalizationOn
            : GlobalizationOff, StringComparison.Ordinal);

    // ICU stops being optional the moment the app names a culture. Under invariant globalization
    // PredefinedCulturesOnly is on, GetCultureInfo("en") cannot produce anything but the invariant
    // culture, and Rask's resolver therefore rejects every configured language — so the app would boot
    // with an EMPTY supported list and warn about it on every start. Scaffolding the catalogs without
    // this property is the no-op this template was corrected for (#846), not a cheaper version of it.
    private const string GlobalizationOn =
        """
        <!-- This app names a language, so it ships ICU. Rask.Wasm.targets turns this one property into
                 InvariantGlobalization=false + PredefinedCulturesOnly=false + full (not EFIGS) ICU.
                 It costs roughly a megabyte on the wire; docs/localization.md has the measurement. -->
            <RaskGlobalization>true</RaskGlobalization>
        """;

    // Left as a commented-out one-liner rather than dropped: an app grows a second language later, and
    // `rask new --culture` is not the only way to get there.
    private const string GlobalizationOff =
        """
        <!-- ICU is dropped by default. Uncomment to ship it, which you need as soon as the app formats
                 dates, numbers or currency per culture, or shows text in more than one language. One
                 property: it also clears PredefinedCulturesOnly, which otherwise defaults to true here and
                 makes CultureInfo.GetCultureInfo("hu-HU") throw rather than fall back. -->
            <!-- <RaskGlobalization>true</RaskGlobalization> -->
        """;

    private const string WasmSdkPropertyGroupTemplate =
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
            @@GLOBALIZATION@@
            <!-- IL2104 comes from Microsoft.JSInterop's reflection-driven [JSInvokable] scanner; apps
                 that only INVOKE JS never hit it. If you mark methods [JSInvokable], add a
                 [DynamicDependency] on them (standard Blazor WASM mitigation) instead of suppressing. -->
            <NoWarn>$(NoWarn);IL2104</NoWarn>
          </PropertyGroup>
        """;

    private static string WasmCsproj(string version, bool localization)
    {
        return $"""
        <Project Sdk="Microsoft.NET.Sdk.WebAssembly">

        {WasmSdkPropertyGroup(localization)}

          <ItemGroup>
            <PackageReference Include="Rask.Wasm" Version="{version}"/>
          </ItemGroup>

        </Project>

        """;
    }

    private static string WasmProgram(bool pwa, IReadOnlyList<string> cultures)
    {
        var sb = new StringBuilder();
        sb.Append("using Company.RaskServer.Features.Shared;\n"); // App lives in the Features/Shared bucket.
        sb.Append("using Rask.Wasm;\n");

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

        sb.Append(WasmUseCulture(cultures));

        sb.Append("\nawait host.RunAsync<App>();\n");
        return sb.ToString();
    }

    /// <summary>
    /// The <c>host.UseCulture</c> block, or nothing at all when the app names no language.
    /// </summary>
    /// <remarks>
    /// The browser half of localization: the runtime already negotiates a visitor's language before the
    /// first render (<c>?culture=</c> → cookie → <c>navigator.languages</c> → the app's default), and this
    /// is the call that tells it which languages there are to choose between. Paired with
    /// <c>&lt;RaskGlobalization&gt;</c> in the csproj, without which no named culture resolves at all.
    /// </remarks>
    internal static string WasmUseCulture(IReadOnlyList<string> cultures)
    {
        if (cultures.Count == 0)
        {
            return string.Empty;
        }

        var languages = string.Join(", ", cultures.Select(culture => $"\"{culture}\""));
        return $$"""

            // The languages this app ships. The FIRST is the default a visitor falls back to when nothing
            // else matches. In the browser their language is negotiated ?culture= -> a remembered cookie ->
            // navigator.languages -> that default, and is settled BEFORE the first render.
            //
            // Text comes from Resources/Strings.{culture}.json, compiled into typed members: a missing key
            // is a build error rather than a blank on the page (docs/diagnostics.md). The csproj ships ICU
            // for this -- see <RaskGlobalization> there, without which none of these names resolve.
            host.UseCulture(c =>
            {
                foreach (var language in new[] { {{languages}} })
                {
                    c.SupportedCultures.Add(language);
                }
            });

            """.TrimStart('\n');
    }

    private static string WasmNextSteps(string name, bool docker, bool localization)
    {
        var steps = new StringBuilder();
        steps.Append("Created ").Append(name).Append(" (Rask browser-WASM SPA).\n\nNext steps:\n");
        steps.Append("  cd ").Append(name).Append('\n');
        steps.Append("  rask dev            # run with hot reload (or: dotnet run)\n");
        if (docker)
        {
            steps.Append("  docker build -t ").Append(name.ToLowerInvariant()).Append(" .   # then: docker run -p 8080:80 …\n");
        }

        if (localization)
        {
            // Said here rather than left to be discovered from a bundle report: this is the one battery on
            // this template that costs download rather than only code, and the number is why it is opt-in.
            steps.Append(
                "\nTranslations live in Resources/Strings.<culture>.json and compile to typed members.\n"
                + "Naming a language ships ICU (<RaskGlobalization> in the csproj), which adds roughly a\n"
                + "megabyte to the published bundle — drop the property to get it back. See docs/localization.md.\n");
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
            # 8080, not 80, so this image matches the server template — `rask deploy`
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
}
