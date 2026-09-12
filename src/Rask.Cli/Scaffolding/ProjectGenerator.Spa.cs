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
    ///     Generates a TypeScript-front-end app: <c>{name}</c> (ASP.NET + CQRS) with a <c>client</c>
    ///     folder inside it, scaffolded by the framework's own tool and then overlaid.
    /// </summary>
    /// <remarks>
    ///     ONE project, not two or three. A C#-on-both-halves solution needs a <c>.Shared</c> because both
    ///     halves are C# and must compile the same record; here the client's half of every contract is
    ///     generated TypeScript, so the messages live in the host and there is nothing for a second .NET
    ///     project to hold — which is why the front end is a folder rather than a sibling, and why the
    ///     host is no longer called <c>.Server</c>: with no sibling, that suffix named nothing.
    /// </remarks>
    public static ScaffoldResult GenerateSpa(
        string targetDirectory,
        string name,
        SpaFramework framework,
        ServerBatteries requested,
        string version,
        DotnetTarget? dotnet = null)
    {
        var batteries = requested.Normalized() with { Cqrs = true };

        // No ExternalScaffolds and no Patches. Both existed only because the client came from
        // `npx create-vite@latest` at scaffold time and was therefore not ours to write: the patches
        // amended somebody else's package.json, .gitignore and index.html in place, precisely because
        // replacing them would have meant carrying a copy of a file we did not own. We own it now —
        // it is committed under src/Rask.Templates/ — so the scaffold needs no network, no Node, and
        // no editing of files it did not write.
        return new ScaffoldResult(
            TemplateMaterializer.Files(
                targetDirectory, framework.Key, name, batteries, version, dotnet ?? DotnetTarget.Default,
                vsCode: true),
            SpaNextSteps(name, framework, batteries.Docker))
        {
            Packages = ["Rask.Cqrs", "Rask.Cqrs.Server", "Rask.Spa.Hosting"],
            RestoreTarget = $"{name}.slnx",
        };
    }

    private static string SpaNextSteps(string name, SpaFramework framework, bool docker)
    {
        var steps = new StringBuilder();
        steps.AppendLine($"Next steps for {name} ({framework.DisplayName}):");
        steps.AppendLine();
        steps.AppendLine($"  cd {name}");
        steps.AppendLine("  rask dev            # the host, and the client's dev server, together");
        steps.AppendLine();
        steps.AppendLine("The first build installs the client's dependencies and writes its generated");
        steps.AppendLine($"contracts into {name}/client/src/rask/ — that directory is gitignored, because it is");
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
    /// <summary>
    ///     Whether Rask writes the client's <c>vite.config.ts</c>.
    /// </summary>
    /// <remarks>
    ///     False for Angular. Angular's build <em>is</em> Vite-based — <c>@angular/build:application</c>
    ///     has run its dev server on Vite since v17 — but the config is Angular's, not yours: the proxy is
    ///     declared in <c>proxy.conf.json</c> and pointed at from <c>angular.json</c>, and writing a
    ///     <c>vite.config.ts</c> beside that would be a file nothing reads.
    /// </remarks>
    public bool WritesViteConfig { get; init; } = true;

    /// <summary>Where the bundler writes, relative to the client.</summary>
    public string DistDir { get; init; } = "dist";

    /// <summary>Where the framework's own dev server listens, for the browser and the banner.</summary>
    public string DevServerUrl { get; init; } = "http://localhost:5173";

    /// <summary>The HTML document the scaffolder wrote, relative to the client project.</summary>
    /// <remarks>
    ///     At the project root for every create-vite template, because Vite treats index.html as the build
    ///     entry point rather than as a static asset. Angular's is under <c>src/</c>. Patching the wrong
    ///     path is not a build error — the patch simply finds no file, and the app ships with no manifest
    ///     link and no service worker.
    /// </remarks>
    public string IndexHtml { get; init; } = "index.html";

    /// <summary>
    ///     The global stylesheet this framework's scaffolder writes, and which its entry point imports.
    /// </summary>
    /// <remarks>
    ///     Not the same file in any two of them — index.css for React, Preact, Solid and Lit, style.css
    ///     for Vue, app.css for Svelte, styles.css for Angular. Overlaying the wrong name does not fail:
    ///     it lands beside the real one, nothing imports it, and the app builds with no Tailwind in it at
    ///     all.
    /// </remarks>
    public string GlobalStylesheet { get; init; } = "src/index.css";

    /// <summary>
    ///     The command that scaffolds the client, given the solution name.
    /// </summary>
    /// <remarks>
    ///     Per framework rather than one <c>create-vite</c> call, because Angular's own scaffolder is
    ///     <c>ng new</c> and that is the one it should get: the whole argument of these templates is that
    ///     the framework's own conventions win.
    /// </remarks>
    public Func<string, IReadOnlyList<string>> Scaffolder { get; init; } =
        static _ => throw new InvalidOperationException("No scaffolder was configured.");

    /// <summary>The tool the scaffolder runs, named in the "install this" message when it is missing.</summary>
    public string ScaffolderName { get; init; } = "create-vite";

    /// <summary>
    ///     The client's name in its own ecosystem's terms: lower-case, dashes, no dots.
    /// </summary>
    /// <remarks>
    ///     Angular validates its project name and rejects <c>Shop.Client</c> outright, so the CLI is given
    ///     <c>shop-client</c> with <c>--directory Shop.Client</c> — which is also what decides where the
    ///     bundle lands, since Angular's default output is <c>dist/&lt;project&gt;/browser</c>.
    /// </remarks>
    internal static string ClientPackageName(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray();
        return new string(chars).Trim('-') + "-client";
    }

    /// <summary>Where this framework's bundle lands for a solution called <paramref name="name" />.</summary>
    public string DistFor(string name) =>
        DistDir.Replace("{client}", ClientPackageName(name), StringComparison.Ordinal);

    /// <summary>
    ///     Ranges rather than exact versions: the client's own lockfile is what makes a build
    ///     reproducible, and pinning exactly here would freeze every scaffolded app on whatever was
    ///     current the day its Rask shipped.
    /// </summary>

    /// <summary>Svelte Query versions independently of the others, and is already at 6.</summary>

    /// <summary>Lit Query is young, and its 0.x means a minor can break — so the range is tighter.</summary>

    /// <summary>The default: ask create-vite for a framework's TypeScript template.</summary>
    private static Func<string, IReadOnlyList<string>> Vite(string template) =>
        _ => ["--yes", "create-vite@latest", "client", "--template", template];

    public static readonly SpaFramework React = new(
        "react", "React", "react-ts",
        "react from '@vitejs/plugin-react'", "react()")
    {
        Scaffolder = Vite("react-ts"),
    };

    /// <summary>Preact, on create-vite's own template.</summary>
    public static readonly SpaFramework Preact = new(
        "preact", "Preact", "preact-ts",
        "preact from '@preact/preset-vite'", "preact()")
    {
        Scaffolder = Vite("preact-ts"),
    };

    public static readonly SpaFramework Solid = new(
        "solid", "Solid", "solid-ts",
        "solid from 'vite-plugin-solid'", "solid()")
    {
        Scaffolder = Vite("solid-ts"),
    };

    public static readonly SpaFramework Vue = new(
        "vue", "Vue", "vue-ts",
        "vue from '@vitejs/plugin-vue'", "vue()")
    {
        GlobalStylesheet = "src/style.css",
        Scaffolder = Vite("vue-ts"),
    };

    /// <summary>Svelte, on create-vite's own template.</summary>
    public static readonly SpaFramework Svelte = new(
        "svelte", "Svelte", "svelte-ts",
        "{ svelte } from '@sveltejs/vite-plugin-svelte'", "svelte()")
    {
        GlobalStylesheet = "src/app.css",
        Scaffolder = Vite("svelte-ts"),
    };

    /// <summary>
    ///     Lit, which needs no Vite plugin at all — its components are standard custom elements, and
    ///     its decorators are TypeScript's.
    /// </summary>
    public static readonly SpaFramework Lit = new(
        "lit", "Lit", "lit-ts",
        string.Empty, string.Empty)
    {
        Scaffolder = Vite("lit-ts"),
    };

    /// <summary>
    ///     Angular, scaffolded by its own CLI.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The one framework here <c>create-vite</c> does not ship a template for. Angular's build is
    ///         Vite-based — <c>@angular/build:application</c> has run its dev server on Vite since v17 —
    ///         but the config belongs to Angular, so there is no <c>vite.config.ts</c> to write: the proxy
    ///         is declared in <c>proxy.conf.json</c> and pointed at from <c>angular.json</c>, and the
    ///         bundle lands in <c>dist/&lt;project&gt;/browser</c> rather than <c>dist</c>.
    ///     </para>
    ///     <para>
    ///         <c>--skip-install</c>: the Rask.Spa.Hosting targets run the install on the first build, the
    ///         way they do for every other client. <c>--skip-git</c> because <c>rask new</c> initialises one
    ///         repository at the solution root, and a second nested inside it is not what anyone wants.
    ///     </para>
    ///     <para>
    ///         Angular's CLI has its own Node floor, higher than Vite's. When it refuses to run it says so
    ///         itself and names the version it wants, which is a better message than one written here.
    ///     </para>
    /// </remarks>
    public static readonly SpaFramework Angular = new(
        "angular", "Angular", string.Empty,
        string.Empty, string.Empty)
    {
        IndexHtml = "src/index.html",
        GlobalStylesheet = "src/styles.css",
        WritesViteConfig = false,
        DistDir = "dist/{client}/browser",
        DevServerUrl = "http://localhost:4200",
        ScaffolderName = "the Angular CLI",
        Scaffolder = name =>
        [
            "--yes", "@angular/cli@latest", "new", ClientPackageName(name),
            "--directory", "client",
            "--style", "css", "--ssr", "false",
            "--skip-git", "--skip-install", "--defaults",
        ],
    };

    /// <summary>Every framework <c>rask new</c> can scaffold a client for.</summary>
    public static IReadOnlyList<SpaFramework> All { get; } = [React, Preact, Vue, Angular, Solid, Svelte, Lit];

    public static bool TryGet(string key, out SpaFramework framework)
    {
        foreach (var candidate in All)
        {
            if (candidate.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                framework = candidate;
                return true;
            }
        }

        framework = React;
        return false;
    }
}
