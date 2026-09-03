namespace Rask.Cli.Scaffolding;

/// <summary>
///     One meta framework <c>rask new</c> can scaffold: how to create it, and the two facts its own
///     creator cannot know.
/// </summary>
/// <remarks>
///     <para>
///         The key is the value that goes into <c>&lt;RaskMetaFramework&gt;</c>, verbatim. One name for
///         the template, the csproj property and the host's baked metadata, because three names for one
///         framework is how a template comes to be accepted by the parser and then generate something
///         else — which is what <c>--template native</c> did after the native host was deleted.
///     </para>
///     <para>
///         The two facts Rask has to supply are the same two for all six. First, the build must emit a
///         <b>node server</b>: this lane runs <c>node &lt;entry&gt;</c>, so a static or edge preset
///         produces an app whose entry never exists and a host that refuses to start naming a path.
///         Second, the dev server must <b>proxy /_rask</b> back to the host, or every dispatch in a
///         <c>rask dev</c> session 404s against the framework's own router.
///     </para>
/// </remarks>
internal sealed record MetaTemplate(
    string Key,
    string DisplayName,
    string ScaffolderName,
    IReadOnlyList<(string Path, string Content)> ConfigFiles)
{
    /// <summary>The command that scaffolds the front end, given the solution name.</summary>
    /// <remarks>
    ///     Each framework's OWN creator, never a generic one: the whole argument of this lane is that the
    ///     framework's conventions win, and a scaffold that produced something its documentation does not
    ///     describe would be worth less than no scaffold at all. Every invocation is non-interactive —
    ///     these creators all prompt by default, and a prompt inside `rask new` is a hang.
    /// </remarks>
    public Func<string, IReadOnlyList<string>> Scaffolder { get; init; } =
        static _ => throw new InvalidOperationException("No scaffolder was configured.");

    /// <summary>Where the build writes the generated contracts and the browser layer, relative to the app.</summary>
    /// <remarks>
    ///     <c>app/</c> for Nuxt and Next's App Router, <c>src/</c> for the rest — whichever directory that
    ///     framework treats as source. It matches <c>_RaskMetaSourceDir</c> in Rask.Meta.Hosting.targets,
    ///     and a mismatch is silent: the files land somewhere the framework does not compile.
    /// </remarks>
    public string GeneratedDir { get; init; } = "src/rask";

    /// <summary>
    ///     The tsconfig to point at the generated <c>tsconfig.rask.json</c>, or null when the framework's
    ///     own config carries the alias instead.
    /// </summary>
    /// <remarks>
    ///     Null for Nuxt alone, and not as an omission: Nuxt GENERATES its tsconfigs into <c>.nuxt/</c> on
    ///     every build and the root one only references them, so an <c>extends</c> written there is not in
    ///     the program that type-checks the app. Nuxt's own <c>alias</c> option is the supported way in,
    ///     and Nuxt propagates it into the tsconfig it writes.
    /// </remarks>
    public string? TsConfigFile { get; init; } = "tsconfig.json";

    /// <summary>
    ///     The Vite config to add the dev proxy to, for a framework whose config Rask must not overwrite.
    /// </summary>
    /// <remarks>
    ///     Patched rather than overlaid because these files are not stubs — they carry work the creator
    ///     just did that this lane depends on. SvelteKit's holds the node adapter and the runes
    ///     compiler option (modern SvelteKit configures kit through the Vite plugin, not through
    ///     svelte.config.js at all), and TanStack's holds the Start plugin and its Nitro deployment.
    ///     Writing our own file over either would delete exactly the thing that makes the build produce
    ///     a node server.
    /// </remarks>
    public string? ViteConfigFile { get; init; }

    /// <summary>
    ///     The folder the front end lives in, inside the host project.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>client</c>, lower case, for every framework on this lane — where the SPA lane uses
    ///         <c>Client</c>. Not a stylistic difference: half of these creators derive an npm package
    ///         name from the target directory and will not accept one with capitals in it.
    ///         <c>create-next-app</c> and <c>@tanstack/cli</c> exit ("name can no longer contain capital
    ///         letters"), and <c>create-analog</c> stops and asks — the worst of the three, because a
    ///         prompt inside <c>rask new</c> is a hang.
    ///     </para>
    ///     <para>
    ///         The csproj carries <c>RaskMetaAppDir</c> to match, so the build and the host look in the
    ///         same place. The case matters on Linux even where macOS would forgive it.
    ///     </para>
    /// </remarks>
    public string AppDir { get; init; } = "client";

    /// <summary>
    ///     The stylesheet Rask writes when the creator cannot be asked for Tailwind, or null when it can.
    /// </summary>
    /// <remarks>
    ///     Four of the six take Tailwind from their own creator, which is the better answer on a lane
    ///     whose argument is that the framework's conventions win: Next has <c>--tailwind</c>, SvelteKit
    ///     an add-on, SolidStart a template, and TanStack includes it in a standard scaffold. Nuxt's
    ///     creator has no such option, and Analog's asks a question it will not take an answer to on the
    ///     command line — so for those two Rask installs it, the same way the SPA lane does.
    /// </remarks>
    public string? TailwindStylesheet { get; init; }

    /// <summary>
    ///     Whether Rask's Tailwind goes in through PostCSS rather than the Vite plugin.
    /// </summary>
    /// <remarks>
    ///     Two adapters for one compiler, and installing the wrong one is silent — nothing reads it, and
    ///     the build succeeds with no utilities in the output. The Vite plugin goes where there is a Vite
    ///     config Rask writes (Nuxt's, through its <c>vite</c> option); PostCSS where the config belongs
    ///     to the framework and is only patched (Analog's), since Vite picks a PostCSS config up on its
    ///     own and no plugin array has to be rewritten.
    /// </remarks>
    public bool TailwindThroughPostcss { get; init; }

    /// <summary>Where this framework's dev server listens, for the next-steps text.</summary>
    public string DevServerUrl { get; init; } = "http://localhost:3000";

    /// <summary>Nuxt's config, replacing the one-line stub the minimal template writes.</summary>
    private const string NuxtConfig =
        """
        import { fileURLToPath } from 'node:url'
        import tailwindcss from '@tailwindcss/vite'

        // https://nuxt.com/docs/api/configuration/nuxt-config
        export default defineNuxtConfig({
          devtools: { enabled: true },

          // Tailwind through its Vite plugin, not the standalone binary: this project already has node
          // and a bundler, and the plugin keeps the bundler's own hot reload for CSS. Nuxt's creator has
          // no option for this, so Rask wires it — the other frameworks on this lane are asked for
          // Tailwind by their own creators instead.
          css: ['~/assets/css/main.css'],
          vite: { plugins: [tailwindcss()] },

          // `@rask/browser/geolocation`, `@rask/client`, `@rask/messages` — the browser layer and the
          // TypeScript projection of your C# message records, written into app/rask/ on every build.
          //
          // Declared here rather than in tsconfig.json, which is the one thing about this lane that is
          // Nuxt-specific: Nuxt GENERATES its tsconfigs into .nuxt/ and the root one only references
          // them, so an `extends` written there is not in the program that type-checks the app. Nuxt
          // propagates these aliases into the config it writes, which is why this is the way in.
          alias: {
            '@rask': fileURLToPath(new URL('./app/rask', import.meta.url)),
          },

          nitro: {
            // The host runs `node .output/server/index.mjs`, so the preset has to be the node one. It is
            // already Nitro's default; named because a preset changed for some other deploy target would
            // otherwise break STARTUP rather than the build, and much later.
            preset: 'node-server',

            // In development the browser talks to Nuxt, and Nuxt forwards the CQRS calls to the ASP.NET
            // host — so HMR is native and there is no CORS to configure, because the browser only ever
            // sees one origin. In production this is not used at all: Kestrel owns the port and answers
            // /_rask itself.
            devProxy: {
              '/_rask': { target: 'http://localhost:5000/_rask', changeOrigin: true },
            },
          },
        })

        """;

    /// <summary>Next's config, replacing the stub whose body is a comment.</summary>
    private const string NextConfig =
        """
        import type { NextConfig } from 'next'

        const nextConfig: NextConfig = {
          // The host runs `node .next/standalone/server.js`, and this is the only output mode that
          // writes it. Without it the build succeeds and startup fails naming a path that was never
          // created.
          //
          // Standalone deliberately omits `public` and `.next/static`, assuming a CDN in front. Here
          // Kestrel IS the thing in front and serves them itself, so that omission costs nothing.
          output: 'standalone',

          // In development the browser talks to Next, and Next forwards the CQRS calls to the ASP.NET
          // host — so HMR is native and there is no CORS to configure. In production this is not used:
          // Kestrel owns the port and answers /_rask itself.
          async rewrites() {
            return [
              { source: '/_rask/:path*', destination: 'http://localhost:5000/_rask/:path*' },
            ]
          },
        }

        export default nextConfig

        """;

    /// <summary>
    ///     Nuxt. Nitro's default preset already emits a node server; it is named anyway, because this
    ///     host runs that entry and a preset changed for a deploy target would otherwise break startup
    ///     rather than the build.
    /// </summary>
    /// <remarks>
    ///     <c>nuxi init</c> refuses to run non-interactively without <c>--template</c>,
    ///     <c>--packageManager</c> and <c>--gitInit</c> — it says so and exits, rather than hanging, which
    ///     is the good failure. All three are passed.
    /// </remarks>
    public static MetaTemplate Nuxt { get; } = new(
        "nuxt", "Nuxt", "nuxi", [("nuxt.config.ts", NuxtConfig)])
    {
        Scaffolder = _ =>
        [
            "--yes", "nuxi@latest", "init", "client",
            "--template", "minimal",
            "--packageManager", "npm",
            "--no-gitInit",

            // The Rask build installs on its first run (npm install, since there is no lockfile yet), so
            // installing here would be the same work done twice and `rask new` would sit on it.
            "--no-install",
        ],
        GeneratedDir = "app/rask",
        TsConfigFile = null,
        TailwindStylesheet = "app/assets/css/main.css",
        DevServerUrl = "http://localhost:3000",
    };

    /// <summary>Next.js. The one framework here whose static assets its own server does not serve.</summary>
    public static MetaTemplate Next { get; } = new(
        "nextjs", "Next.js", "create-next-app", [("next.config.ts", NextConfig)])
    {
        Scaffolder = _ =>
        [
            "--yes", "create-next-app@latest", "client",
            "--ts", "--app", "--no-src-dir", "--no-eslint", "--tailwind",
            "--use-npm", "--skip-install", "--disable-git", "--yes",
        ],
        GeneratedDir = "app/rask",
        DevServerUrl = "http://localhost:3000",
    };

    /// <summary>
    ///     SvelteKit, through its own <c>sveltekit-adapter</c> add-on rather than a package.json patch.
    /// </summary>
    /// <remarks>
    ///     <c>sv create --add sveltekit-adapter=adapter:node</c> installs adapter-node AND configures it,
    ///     which is why nothing is overlaid here. Modern SvelteKit configures kit through the Vite
    ///     plugin — the scaffold writes no svelte.config.js at all — so the adapter and the runes option
    ///     live in the same vite.config.ts the dev proxy is patched into. Writing that file ourselves
    ///     would delete both.
    /// </remarks>
    public static MetaTemplate SvelteKit { get; } = new(
        "sveltekit", "SvelteKit", "sv", [])
    {
        Scaffolder = _ =>
        [
            "--yes", "sv@latest", "create", "client",
            "--template", "minimal", "--types", "ts",
            "--add", "sveltekit-adapter=adapter:node", "tailwindcss=plugins:typography",
            "--no-install", "--no-dir-check", "--no-download-check",
        ],
        ViteConfigFile = "vite.config.ts",
        DevServerUrl = "http://localhost:5173",
    };

    /// <summary>
    ///     TanStack Start, through <c>@tanstack/cli</c> — <c>create-start-app</c> is deprecated and says
    ///     so on every run.
    /// </summary>
    /// <remarks>
    ///     <c>--deployment nitro</c> is what makes this lane work: it wires Nitro into the Vite config,
    ///     so the build emits <c>.output/server/index.mjs</c>. Any of the other deployment adapters this
    ///     CLI offers (cloudflare, netlify, vercel…) produces something this host cannot run.
    /// </remarks>
    public static MetaTemplate TanStackStart { get; } = new(
        "tanstack-start", "TanStack Start", "@tanstack/cli", [])
    {
        Scaffolder = _ =>
        [
            "--yes", "@tanstack/cli", "create", "client",
            "--framework", "react",
            "--deployment", "nitro",
            "--non-interactive", "--no-install", "--no-git",
        ],
        ViteConfigFile = "vite.config.ts",
        DevServerUrl = "http://localhost:3000",
    };

    /// <summary>
    ///     SolidStart, version 2 — which is a Vite app, not the <c>app.config.ts</c> shape version 1 had.
    /// </summary>
    /// <remarks>
    ///     Its creator asks two questions before it will do anything, and both have to be answered on the
    ///     command line: <c>--v2</c> for the major version and <c>-t basic</c> for the template. Miss
    ///     either and it sits on a prompt, which inside <c>rask new</c> is a hang rather than an error.
    /// </remarks>
    public static MetaTemplate SolidStart { get; } = new(
        "solidstart", "SolidStart", "create-solid", [])
    {
        Scaffolder = _ =>
        [
            "--yes", "create-solid@latest", "client",
            "--solidstart", "--v2", "--ts", "-t", "with-tailwindcss",
        ],
        ViteConfigFile = "vite.config.ts",

        // Vite's own default is 5173, but @solidjs/start moves it — a scaffolded app reports
        // "Local: http://localhost:3000". Measured rather than assumed, because --open goes here.
        DevServerUrl = "http://localhost:3000",
    };

    /// <summary>Analog, the Angular meta framework.</summary>
    /// <remarks>
    ///     <para>
    ///         <c>--skipTailwind</c> is load-bearing and undocumented in any <c>--help</c>, because
    ///         <c>create-analog</c> has none: it answers <c>--help</c> with its first prompt. Without
    ///         that flag it asks about Tailwind and waits, and neither <c>--yes</c> nor
    ///         <c>--no-tailwind</c> is understood.
    ///     </para>
    ///     <para>
    ///         It also refuses a nested path. Given <c>Shop/Client</c> — or <c>shop/client</c>; the
    ///         casing is not what does it — it stops and asks for a package name, and no flag answers
    ///         that question: <c>--name</c>, <c>--packageName</c> and <c>--skipPackageName</c> are all
    ///         simply ignored. So it is the one creator run from INSIDE the project directory, with a
    ///         target of one segment. See <see cref="ExternalScaffold.WorkingSubdirectory" />.
    ///     </para>
    ///     <para>
    ///         Adding Analog to an Angular app instead was tried and does not work: the package installs
    ///         and reports "does not provide any `ng add` actions", leaving an ordinary Angular app with
    ///         a dependency and none of the wiring.
    ///     </para>
    /// </remarks>
    public static MetaTemplate Analog { get; } = new(
        "analog", "Analog", "create-analog", [])
    {
        Scaffolder = _ =>
        [
            "--yes", "create-analog@latest", "client",
            "--template", "angular-v20", "--skipTailwind",
        ],
        TailwindStylesheet = "src/styles.css",
        TailwindThroughPostcss = true,
        ViteConfigFile = "vite.config.ts",
        DevServerUrl = "http://localhost:5173",
    };

    /// <summary>Every framework this lane scaffolds, keyed by <c>RaskMetaFramework</c>'s own names.</summary>
    public static IReadOnlyList<MetaTemplate> All { get; } =
        [Nuxt, Next, SvelteKit, SolidStart, TanStackStart, Analog];

    public static bool TryGet(string key, out MetaTemplate template)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                template = candidate;
                return true;
            }
        }

        template = null!;
        return false;
    }
}
