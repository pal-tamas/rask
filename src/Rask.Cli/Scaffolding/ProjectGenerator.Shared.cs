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
        using Rask.Core.Live;
        using Rask.Core.Routing;
        using Rask.Ui;

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

            // Turns the UI kit's theme on for the whole document.
            //
            // Load-bearing, and its absence is silent: daisyUI paints :root by default and the kit
            // confines its palette to this attribute, so that referencing the package cannot repaint an
            // app that only wanted a button. Without it every Ui* component renders structurally
            // correct and completely grey.
            //
            // Put `data-theme` here too to pick one of daisyUI's 35 themes; the default is light, with
            // dark following the operating system.
            protected override Component Shell(Component head, Component body) =>
                Html.Lang(HtmlLang).Dir(HtmlDir).Attributes((UiStylesheet.ThemeScopeAttribute, ""))[
                    head,
                    Body.Class(BodyClass)[body]
                ];
        }

        """;

    // Two sheets, and the ORDER IS THE CONTRACT.
    //
    // A browser ranks @layer names by FIRST APPEARANCE, across every sheet on the page, in link order —
    // and nothing later can reorder a name already placed. The kit's sheet opens by declaring the order
    // it means, so it has to arrive first; link it second and the ranking falls out of whichever sheet
    // happened to mention a name earliest. That is not a hypothetical: it put `base` above `utilities`
    // for a whole document once, and every text-4xl and px-* in the markup was silently beaten by
    // preflight's `h1 { font-size: inherit }` and `* { padding: 0 }`.
    private const string TailwindHead =
        """
                // The kit's sheet, FIRST — it declares the @layer order for the whole document.
                // Href() carries a content hash, so it caches hard and still changes when the kit does.
                Link.Rel("stylesheet").Href(UiStylesheet.Href(LiveOptions.PathBase)),
                // Compiled from Styles/app.css by Rask.Tailwind, scanning this project's own source.
                Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/css/app.css")
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

    // The starter page, in daisyUI's own class names.
    //
    // Every class here is one Tailwind will find by scanning THIS FILE at build time — which is the
    // whole mechanism, and the reason the page is worth scaffolding rather than leaving the stylesheet
    // empty: it proves the loop end to end on the first build, plugin included.
    //
    // Spelled out as complete literals, never assembled. daisyUI emits a component's CSS only where
    // Tailwind can SEE the class name, so a name built by concatenation ("btn-" + tone) is absent from
    // the sheet and the component renders with NO styling at all, on a green build.
    //
    // The same navbar / hero / card / footer skeleton is what every other `rask new` template draws,
    // down to the class names, so a project looks the same whichever front end it was scaffolded with.
    private static string HomePageTailwindCs(bool accounts) =>
        $$"""
        using Rask.Core.Routing;

        namespace Company.RaskServer.Features.Home;

        [Route("/")]
        public sealed partial class HomePage : Component
        {
            protected override Component? Render() =>
                // A column so the footer sits at the bottom of a short page rather than under the fold.
                Div.Class("flex min-h-screen flex-col bg-base-200")[
                    Nav.Class("navbar bg-base-100 shadow-sm")[
                        Div.Class("navbar-start")[
                            Span.Class("px-2 text-lg font-semibold tracking-tight")["Company.RaskServer"]
                        ],
                        Div.Class("navbar-end")[{{(accounts ? SignInLink : DocsLink)}}]
                    ],
                    Main.Class("hero grow bg-base-200 py-16")[
                        Div.Class("hero-content text-center")[
                            Div.Class("max-w-md")[
                                H1.Class("text-4xl font-bold")["Hello, Rask! 👋"],
                                P.Class("py-4 text-base-content/70")["Your app is running. What to do next:"],
                                Div.Class("card bg-base-100 w-full max-w-md shadow-sm")[
                                    Div.Class("card-body gap-4 text-left")[
                                        Ul.Class("space-y-2 text-sm")[
                                            Li[Code.Class("kbd kbd-sm")["rask dev"], " — run with hot reload"],
                                            Li[Code.Class("kbd kbd-sm")["rask db add Init"], " — create the database"],
                                            Li["Edit ", Code.Class("kbd kbd-sm")["HomePage.cs"], " — the sheet rebuilds from it"]
                                        ],
                                        Div.Class("card-actions justify-end")[
                                            A
                                                .Class("btn btn-primary")
                                                .Href("https://rask.sh/docs/tutorial/00-overview")["Start the tutorial"]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ],
                    Footer.Class("footer footer-center bg-base-100 p-4 text-base-content/70")[
                        Aside[P["Built with Rask."]]
                    ]
                ];
        }

        """;

    /// <summary>
    ///     The two opt-ins that put daisyUI in a scaffolded app's own build, and the kit's sheet on its
    ///     pages. Both default to <c>false</c> in <c>Rask.Ui</c>, because referencing a component kit is
    ///     not the same as asking it to write files into your project.
    /// </summary>
    /// <remarks>
    ///     They answer two different questions and an app needs both. The stylesheet is what styles the
    ///     <c>Ui*</c> components, whose class names live in a compiled assembly no Tailwind can scan.
    ///     The plugin is what styles the daisyUI class names this project writes in its OWN markup,
    ///     which the kit's prebuilt sheet knows nothing about.
    /// </remarks>
    private const string UiKitProperties =
        """
            <!-- The UI kit's compiled sheet as a cached file in wwwroot, rather than inlined in every
                 document. Linked FIRST in Features/Shared/App.cs: it declares the @layer order. -->
            <RaskUiWriteStylesheet>true</RaskUiWriteStylesheet>
            <!-- daisyUI's plugin, copied beside Styles/app.css so this project compiles daisyUI itself.
                 No npm and no node_modules: `dotnet build` is still the whole toolchain. -->
            <RaskUiWriteDaisyUiPlugin>true</RaskUiWriteDaisyUiPlugin>
        """;

    // An app with a database has accounts, so the built-in sign-in page is reachable and the starter
    // says so. Without one there is nothing at /login, and a link to it would be a dead end.
    private const string SignInLink =
        """
        A.Class("btn btn-primary btn-sm").Href("/login")["Sign in"]
        """;

    private const string DocsLink =
        """
        A.Class("link link-hover link-primary").Href("https://rask.sh/docs")["Docs"]
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

        # The front-end templates' dependencies and build output. The image runs its own `npm ci`, and
        # `COPY . .` happens AFTER it — so a developer's host node_modules would land on top of the
        # container's, replacing linux binaries (esbuild, rollup, @tailwindcss/oxide) with darwin or
        # windows ones. The publish then fails inside the image with a platform mismatch, or ships a
        # tree that cannot run.
        **/node_modules/
        **/.output/
        **/.next/
        **/.nuxt/
        **/.svelte-kit/
        **/dist/

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

        # Written into the tree by the build, not by you: the compiled Tailwind sheet, the UI kit's
        # own sheet, and the daisyUI plugin Rask.Ui ships so this project can compile daisyUI itself.
        wwwroot/css/app.css
        wwwroot/css/rask-ui.css
        Styles/vendor/

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
        /*
          The layer order for the whole document, declared before anything can imply another one.

          daisyUI emits its rules into a `daisyui` layer, and Tailwind's own import only ranks
          theme/base/components/utilities — so `daisyui` would otherwise be ranked by wherever it
          first appeared in the output, which lands it ABOVE utilities. That makes `class="btn px-8"`
          give you .btn's padding and not px-8: correct markup, quietly ignored.
        */
        @layer properties, theme, base, components, daisyui, utilities;

        @import "tailwindcss";

        /*
          The plugin bundle is NOT a source file, and saying so is load-bearing.

          Tailwind scans the project it runs in, and vendor/daisyui.mjs is inside it: daisyUI's own
          code, naming every class daisyUI defines. Scanned, it acts as a safelist for the whole
          library and this sheet carries every component whether or not you use one — which reads as
          correct, because a sheet containing too much looks exactly like a sheet containing enough.
        */
        @source not "./vendor";

        /*
          daisyUI, compiled from the copy Rask.Ui ships — no npm, no node_modules, no package.json.
          The build copies it here; it is generated, and .gitignore'd for the same reason wwwroot is.

          By relative path because Tailwind resolves a plugin the way Node does, by walking up for a
          node_modules, and the standalone engine carries no package tree.
        */
        @plugin "./vendor/daisyui.mjs";

        /* Your own CSS goes here. Anything below participates in the same build, so @apply and
           @theme work, and the output still contains only what this project actually uses.

           Redefining a daisyUI token in your own @theme re-skins every component without overriding
           a single rule — that is the intended way to make this yours. */

        """;
}
