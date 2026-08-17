using System.Globalization;
using System.Text;

namespace Rask.Cli.Scaffolding;

// Template content shared by more than one template, emitted verbatim with the Company.RaskServer
// namespace token replaced centrally (see ProjectGenerator.Materialize).
internal static partial class ProjectGenerator
{
    /// <summary>
    ///     The <c>Program.cs</c> shutdown block, shared by every web template and derived from
    ///     <see cref="ShutdownBudget"/> rather than hardcoded — the app's budget and the deploy's grace used
    ///     to be two literals coupled only by a comment, free to drift apart silently.
    /// </summary>
    /// <param name="fileBasedDatabase">
    ///     True when the app's database is a file this process owns (SQLite), so the budget also has to
    ///     cover a WAL checkpoint and a Litestream flush. A client-server database has neither, and saying
    ///     it does would send someone sizing their shutdown budget after work that never happens.
    /// </param>
    private static string ShutdownBudgetBlock(bool fileBasedDatabase)
    {
        // Pre-converted so the interpolation below formats nothing — the raw literal already contains the
        // braces of a lambda body, which is why it is $$"""…""" with {{…}} holes.
        var dockerStop = ShutdownBudget.DockerStopSeconds.ToString(CultureInfo.InvariantCulture);
        var hostShutdown = ShutdownBudget.HostShutdownSeconds.ToString(CultureInfo.InvariantCulture);
        var drainTail = fileBasedDatabase
            ? "live\n        // sessions close cleanly, and a SQLite WAL checkpoint / Litestream flush complete instead of\n        // being killed mid-write."
            : "live\n        // sessions close cleanly, and in-flight work finishes instead of being cut off mid-request.";
        // Litestream only exists on the SQLite path, so it must not head the example list otherwise.
        var graceExamples = fileBasedDatabase
            ? "Litestream's WAL flush, an in-flight email send, a"
            : "an in-flight email send, a";

        return $$"""

        // Finish shutting down before the container runtime loses patience. `rask deploy` sends SIGTERM and
        // SIGKILLs {{dockerStop}}s later, so a budget under that is what lets in-flight requests drain, {{drainTail}}
        //
        // ServicesStopConcurrently matters as much as the number: stopped one at a time (the .NET default)
        // each hosted service's own shutdown grace — {{graceExamples}}
        // running job — SUMS inside this one budget, and whichever stops last gets none of it, decided by
        // the order of your AddRaskX calls. Stopped together they overlap instead.
        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds({{hostShutdown}});
            options.ServicesStopConcurrently = true;
        });

        """.TrimStart('\n');
    }

    // The app shell every page renders through (RASK021), living in Features/Shared/ — the cross-cutting
    // bucket a new project shares across its feature slices. The welcome home page is its own Features/Home
    // slice (see HomePageCs). With Bootstrap the styling comes from the CDN-free Rask.Bootstrap asset;
    // without it the shell carries a small baseline of its own, so an opted-out app is still presentable.
    private static string AppShellCs(bool bootstrap) =>
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
        {{(bootstrap ? BootstrapHead : BaselineHead)}}
            ];

            // The body's content. Rask emits the doctype, <html lang>, <head> and <body> around this —
            // override HtmlLang / BodyClass for their attributes, or Shell(head, body) for the rest.
            protected override Component? Render() => Router;
        }

        """;

    private const string BootstrapHead =
        """
                // Bootstrap 5.3 + Icons via Rask.Bootstrap (served from _content/Rask.Bootstrap).
                BootstrapStyles
        """;

    // No CSS framework: a baseline inline in the shell rather than a stylesheet file, so it works the same
    // on every template (a server app, a WASM bundle, a native WebView) with nothing extra to serve. Raw()
    // because CSS is not HTML — encoding it would break every selector containing > or &.
    private const string BaselineHead =
        """"
                // A small baseline of our own — no CSS framework. Replace this with yours.
                Style[Raw.Value("""
                    :root { color-scheme: light dark; --ink: #1c1b22; --muted: #5c5a6b; --bg: #faf9fe;
                            --card: #ffffff; --line: #e6e4f0; --brand: #512BD4; }
                    @media (prefers-color-scheme: dark) {
                        :root { --ink: #eceaf5; --muted: #a5a2b8; --bg: #16151c; --card: #201f29; --line: #322f42; }
                    }
                    * { box-sizing: border-box; }
                    body { margin: 0; padding: 2rem 1rem; background: var(--bg); color: var(--ink);
                           font: 16px/1.6 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
                    main { max-width: 34rem; margin: 0 auto; }
                    .card { background: var(--card); border: 1px solid var(--line); border-radius: 12px;
                            padding: 1.75rem; box-shadow: 0 1px 3px rgb(0 0 0 / 6%); }
                    h1 { margin: 0 0 .5rem; font-size: 1.5rem; letter-spacing: -0.01em; }
                    p { margin: 0 0 1rem; color: var(--muted); }
                    ul { margin: 0 0 1rem; padding-left: 1.25rem; }
                    li { margin-bottom: .35rem; }
                    code { background: color-mix(in srgb, var(--brand) 10%, transparent); color: var(--brand);
                           padding: .15em .4em; border-radius: 5px; font-size: .875em; }
                    a { color: var(--brand); }
                    .small { font-size: .875rem; }
                    """)]
        """";

    // The welcome home page that teaches the CLI — a Features/Home slice, so a new project already models
    // the "screens are feature slices" convention the CLI generates into.
    private static string HomePageCs(bool bootstrap) => bootstrap ? HomePageBootstrapCs : HomePageBaselineCs;

    private const string HomePageBootstrapCs =
        """
        using Rask.Core.Routing;

        namespace Company.RaskServer.Features.Home;

        public sealed partial class HomePage : Page
        {
            protected override string Route => "/";

            // BsBlock exposes only Id/Class (not Element's full HTML surface), so the width lives on a
            // plain Div wrapper rather than a .Style() on the card.
            protected override Component? Render() =>
                Div.Class("mx-auto my-5").Style("max-width:540px")[
                    BsCard.Class("shadow-sm")[
                        BsCardBody[
                            BsCardTitle["Hello, Rask! 👋"],
                            BsCardText.Class("text-body-secondary")["Your app is ready. What to do next:"],
                            Ul.Class("mb-3")[
                                Li[Code["rask dev"], " — run with hot reload"],
                                Li[Code["rask db add Init"], " then ", Code["rask db update"], " — create the database"],
                                Li[A.Href("https://github.com/pal-tamas/rask/blob/main/docs/tutorial/02-first-feature.md")["Build your first feature"], " — entity, pages and CQRS handlers, step by step"]
                            ],
                            P.Class("mb-0 small text-body-secondary")[
                                "Edit this page in ",
                                Code["HomePage.cs"],
                                " — drop a ",
                                Code["HomePage.css"],
                                " beside it and its rules are scoped to this page. Full guides at ",
                                A.Href("https://github.com/pal-tamas/rask")["the Rask docs"],
                                "."
                            ]
                        ]
                    ]
                ];
        }

        """;

    // The same page in plain elements, against the shell's baseline CSS — no component library, so every
    // class here is one the project owns and can rename.
    private const string HomePageBaselineCs =
        """
        using Rask.Core.Routing;

        namespace Company.RaskServer.Features.Home;

        public sealed partial class HomePage : Page
        {
            protected override string Route => "/";

            protected override Component? Render() =>
                Main[
                    Div.Class("card")[
                        H1["Hello, Rask! 👋"],
                        P["Your app is ready. What to do next:"],
                        Ul[
                            Li[Code["rask dev"], " — run with hot reload"],
                            Li[Code["rask db add Init"], " then ", Code["rask db update"], " — create the database"],
                            Li[A.Href("https://github.com/pal-tamas/rask/blob/main/docs/tutorial/02-first-feature.md")["Build your first feature"], " — entity, pages and CQRS handlers, step by step"]
                        ],
                        P.Class("small")[
                            "Edit this page in ",
                            Code["HomePage.cs"],
                            " — drop a ",
                            Code["HomePage.css"],
                            " beside it and its rules are scoped to this page. Full guides at ",
                            A.Href("https://github.com/pal-tamas/rask")["the Rask docs"],
                            "."
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

    private const string AuthCredentialStore =
        """
        using System.Security.Claims;

        namespace Company.RaskServer.Features.Auth;

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
    /// files that carry secrets, and the app's own SQLite database — a committed <c>app.db</c> is the most
    /// common way a scaffolded repo ends up with real data in its history.
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
