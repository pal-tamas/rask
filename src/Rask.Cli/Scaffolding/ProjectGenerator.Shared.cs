using System.Text;

namespace Rask.Cli.Scaffolding;

// Template content shared by more than one template, emitted verbatim with the Company.RaskServer
// namespace token replaced centrally (see ProjectGenerator.Materialize).
internal static partial class ProjectGenerator
{
    // The app shell every page renders through (RASK021), living in Features/Shared/ — the cross-cutting
    // bucket a new project shares across its feature slices. The welcome home page is its own Features/Home
    // slice. Styling is Tailwind, unconditionally: it is a battery like any other, so there is no axis to
    // choose along and no unstyled path to keep presentable.
    /// <summary>
    /// One catalog per language, under the <c>Resources/</c> directory <c>Rask.Core.targets</c> globs into
    /// <c>&lt;AdditionalFiles&gt;</c> — which is why this works unchanged on a browser-WASM project.
    /// </summary>
    /// <remarks>
    /// The FIRST language is the neutral one: it defines which keys exist, and its text is what a visitor
    /// sees until a translation is filled in. <paramref name="prefix"/> re-homes the whole set into a
    /// sub-project, which is what a front-end template's client needs.
    /// </remarks>
    private static IEnumerable<(string Path, string Content)> StringCatalogs(
        IReadOnlyList<string> cultures, string prefix = "")
    {
        for (var i = 0; i < cultures.Count; i++)
        {
            yield return ($"{prefix}Resources/Strings.{cultures[i]}.json", StringsCatalog(i == 0));
        }
    }

    private static string AppShellCs() =>
        $$"""
        using Rask.Core.Routing;

        namespace Company.RaskServer.Features.Shared;

        public sealed partial class App : Component
        {
            // App-level head contributions splice into the framework-managed <head>
            // via the Component? HeadAssets override. Title is singleton — any page that
            // overrides HeadAssets with its own Title supersedes this fallback for the tab.
            protected override Component? HeadAssets => [
                Title["Company.RaskServer"],
                Meta.Charset("utf-8"),
                Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        {{TailwindHead}}
            ];

            // The body's content. Rask emits the doctype, <html lang>, <head> and <body> around this —
            // override HtmlLang / BodyClass for their attributes, or Shell(head, body) for the rest.
            protected override Component? Render() => Router;
        }

        """;

    // A plain <link> to what the build compiled. Nothing framework-specific: Rask.Tailwind writes
    // wwwroot/css/app.css before the app builds, and every host already serves wwwroot.
    private const string TailwindHead =
        """
                // Compiled from Styles/app.css by Rask.Tailwind, scanning this project's own source.
                Link.Rel("stylesheet").Href("/css/app.css")
        """;

    // The welcome home page that teaches the CLI — a Features/Home slice, so a new project already models
    // the "screens are feature slices" convention the CLI generates into.
    /// <summary>
    ///     The tsconfig scaffolded projects get, so an editor type-checks scoped TypeScript the way the
    ///     gate does.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The BUILD does not read this. Rask compiles scoped assets by handing tsgo an explicit file
    ///         list and explicit flags, which is what keeps the emitted form the one
    ///         <c>ScopedAssetRegistry</c> parses. This file exists for the editor, and the difference
    ///         matters: without it a scoped <c>.ts</c> gets no checking and no completion for
    ///         <c>window.Rask</c> or <c>window.DotNet</c>, so the author sees the guarantee only when
    ///         they run the gate — which is most of it thrown away.
    ///     </para>
    ///     <para>
    ///         <c>obj/rask/types</c> is where the build stages Rask's ambient declarations. The real file
    ///         ships inside the NuGet package, under a versioned cache directory no tsconfig can name, so
    ///         a staged copy is what makes it reachable. It appears after the first build.
    ///     </para>
    ///     <para>
    ///         <c>noEmit</c>, because tsgo writes the output and it writes it elsewhere. An editor that
    ///         decided to emit would drop a <c>.js</c> beside the <c>.ts</c> — which is RASK055, and a
    ///         confusing way to meet it.
    ///     </para>
    /// </remarks>
    private const string TsConfigJson =
        """
        {
          "compilerOptions": {
            "target": "es2020",
            "module": "esnext",
            "moduleResolution": "bundler",
            "lib": ["es2020", "dom"],
            "strict": true,
            "noUnusedLocals": true,
            "noEmit": true,
            "skipLibCheck": true
          },
          "include": ["**/*.ts", "obj/rask/types/**/*.d.ts"],
          "exclude": ["bin", "obj/Debug", "obj/Release", "node_modules", "wwwroot"]
        }

        """;

    // The same page in Tailwind utilities. Every class here is one Tailwind will find by scanning THIS
    // FILE at build time — which is the whole mechanism, and the reason the page is worth scaffolding
    // rather than leaving the stylesheet empty: it proves the loop end to end on the first build.
    private const string HomePageTailwindCs =
        """
        using Rask.Core.Routing;

        namespace Company.RaskServer.Features.Home;

        [Route("/")]
        public sealed partial class HomePage : Component
        {
            protected override Component? Render() =>
                Main.Class("mx-auto max-w-xl px-4 py-10")[
                    Div.Class("rounded-xl border border-slate-200 bg-white p-7 shadow-sm dark:border-slate-700 dark:bg-slate-800")[
                        H1.Class("mb-2 text-2xl font-semibold tracking-tight")["Hello, Rask! 👋"],
                        P.Class("mb-4 text-slate-500 dark:text-slate-400")["Your app is ready. What to do next:"],
                        Ul.Class("mb-4 list-disc space-y-1 pl-5")[
                            Li[Code.Class("rounded bg-violet-100 px-1.5 py-0.5 text-violet-700")["rask dev"], " — run with hot reload"],
                            Li[Code.Class("rounded bg-violet-100 px-1.5 py-0.5 text-violet-700")["rask db add Init"], " — create the database"],
                            Li[A.Class("text-violet-600 underline underline-offset-2 hover:text-violet-500").Href("https://github.com/pal-tamas/rask/blob/main/docs/tutorial/02-first-feature.md")["Build your first feature"]]
                        ],
                        P.Class("text-sm text-slate-500 dark:text-slate-400")[
                            "Edit this page in ",
                            Code.Class("rounded bg-slate-100 px-1.5 py-0.5 dark:bg-slate-700")["HomePage.cs"],
                            " — Tailwind rebuilds the stylesheet from it on the next build."
                        ]
                    ]
                ];
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

    /// <summary>
    /// The solution file, in the XML <c>.slnx</c> format the .NET SDK reads directly. It replaces the old
    /// <c>.sln</c> with something a human can edit and a merge can resolve: a list of project paths, and no
    /// per-project GUIDs or configuration matrix to keep in sync by hand.
    /// </summary>
    /// <param name="projectPaths">Project paths relative to the solution, in the order they should appear.</param>
    private static string Slnx(params IReadOnlyList<string> projectPaths)
    {
        var builder = new StringBuilder("<Solution>\n");
        foreach (var path in projectPaths)
        {
            // .slnx paths are written with forward slashes on every platform.
            builder.Append("  <Project Path=\"").Append(path.Replace('\\', '/')).Append("\" />\n");
        }

        return builder.Append("</Solution>\n").ToString();
    }

    /// <summary>
    /// Every scaffolded project's <c>.gitignore</c>. Deliberately short: build output, IDE and OS noise, the
    /// files that carry secrets, and what the app itself writes — a committed <c>app.db</c> is the most
    /// common way a scaffolded repo ends up with real data in its history, and now that every battery is on
    /// by default the log store, the mail pickup directory and the snapshot directory appear the first time
    /// the app runs rather than only in an app that asked for them.
    /// </summary>
    private const string GitIgnore =
        """
        # Build output
        bin/
        obj/
        [Bb]uild/
        [Oo]ut/
        artifacts/

        # IDE / editor
        .vs/
        .vscode/
        .idea/
        *.user
        *.suo
        *.userosscache

        # OS
        .DS_Store
        Thumbs.db

        # Local configuration and secrets — appsettings.Development.json and .env hold connection
        # strings and API keys. Keep the templates (appsettings.json, .env.example) tracked instead.
        appsettings.Development.json
        appsettings.Local.json
        .env
        !.env.example

        # The app's own database, and SQLite's write-ahead log alongside it
        *.db
        *.db-shm
        *.db-wal

        # What the batteries write beside the app. They are all on unless Program.cs says otherwise, so
        # these appear the first time it runs: queued mail with no SMTP configured is written here as
        # .eml files you can open, and the scheduled point-in-time backups land here.
        mail-pickup/
        snapshots/

        # Publish output
        publish/
        *.nupkg

        """;

    /// <summary>
    /// Every scaffolded project's <c>.editorconfig</c>. This is the file that makes a repo's formatting a
    /// property of the repo rather than of whoever last opened it: <c>dotnet format</c>, Visual Studio,
    /// Rider and VS Code all read it, so a contributor with different defaults still produces the same diff.
    /// </summary>
    private const string EditorConfig =
        """
        # Formatting for this repository. `dotnet format` applies it; every major C# editor honours it.
        root = true

        [*]
        charset = utf-8
        end_of_line = lf
        indent_style = space
        indent_size = 2
        insert_final_newline = true
        trim_trailing_whitespace = true

        [*.{cs,csx}]
        indent_size = 4

        # Compiler diagnostics that catch real bugs, raised from suggestion to warning so they show up in
        # a normal build rather than only under an IDE lightbulb.
        dotnet_diagnostic.CA2007.severity = none
        dotnet_diagnostic.IDE0005.severity = warning

        # Language style
        csharp_style_namespace_declarations = file_scoped:warning
        csharp_using_directive_placement = outside_namespace:warning
        csharp_prefer_braces = true:warning
        csharp_style_var_for_built_in_types = false:suggestion
        csharp_style_var_when_type_is_apparent = true:suggestion
        dotnet_sort_system_directives_first = true
        dotnet_separate_import_directive_groups = false
        dotnet_style_require_accessibility_modifiers = for_non_interface_members:warning
        dotnet_style_readonly_field = true:warning
        dotnet_style_prefer_is_null_check_over_reference_equality_method = true:warning

        # Naming: interfaces start with I, types and members are PascalCase.
        dotnet_naming_rule.interfaces_start_with_i.symbols = interface_symbols
        dotnet_naming_rule.interfaces_start_with_i.style = prefixed_with_i
        dotnet_naming_rule.interfaces_start_with_i.severity = warning
        dotnet_naming_symbols.interface_symbols.applicable_kinds = interface
        dotnet_naming_style.prefixed_with_i.required_prefix = I
        dotnet_naming_style.prefixed_with_i.capitalization = pascal_case

        [*.{json,yml,yaml,xml,csproj,slnx,props,targets}]
        indent_size = 2

        [*.md]
        trim_trailing_whitespace = false

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
}

internal static partial class ProjectGenerator
{
    /// <summary>
    ///     The stylesheet Tailwind compiles, and the only CSS file a Tailwind project starts with.
    /// </summary>
    /// <remarks>
    ///     One import, because that is genuinely all v4 needs — no config file, no <c>content</c> array,
    ///     no PostCSS. Tailwind detects the sources itself from the project directory, which is why the
    ///     C# pages are scanned with nothing telling it to.
    /// </remarks>
    private const string TailwindInputCss =
        """
        @import "tailwindcss";

        /* Your own CSS goes here. Anything below participates in the same build, so @apply and
           @theme work, and the output still contains only what this project actually uses. */

        """;
}
