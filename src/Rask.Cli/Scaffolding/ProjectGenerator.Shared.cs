using System.Globalization;

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
    // bucket a new project shares across its feature slices. Styled with Bootstrap so there is no scoped
    // .css to pair. The welcome home page is its own Features/Home slice (see HomePageCs).
    private const string AppShellCs =
        """
        using Rask.Core.Routing;

        namespace Company.RaskServer.Features.Shared;

        public sealed partial class App : Component
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
                        Body()[Router()]
                    ]
                ];
        }

        """;

    // The welcome home page that teaches the CLI — a Features/Home slice, so a new project already models
    // the "screens are feature slices" convention the CLI generates into.
    private const string HomePageCs =
        """
        using Rask.Core.Routing;

        namespace Company.RaskServer.Features.Home;

        [Route("/")]
        public sealed partial class HomePage : Component
        {
            // BsBlock exposes only Id/Class (not Element's full HTML surface), so the width lives on a
            // plain Div wrapper rather than a Style: on the card.
            protected override Component? Render() =>
                Div(Class: "mx-auto my-5", Style: "max-width:540px")[
                    BsCard(Class: "shadow-sm")[
                        BsCardBody()[
                            BsCardTitle()["Hello, Rask! 👋"],
                            BsCardText(Class: "text-body-secondary")["Your app is ready. Scaffold the rest with the rask CLI:"],
                            Ul(Class: "mb-3")[
                                Li()[Code()["rask generate feature Product Name:string Price:decimal"], " — a full CRUD slice (entity, pages, tests)"],
                                Li()[Code()["rask generate page About"], " — a routed page"],
                                Li()[Code()["rask generate component Card"], " — a reusable component"],
                                Li()[Code()["rask dev"], " — run with hot reload"]
                            ],
                            P(Class: "mb-0 small text-body-secondary")[
                                "Edit this page in ",
                                Code()["HomePage.cs"],
                                " — drop a ",
                                Code()["HomePage.css"],
                                " beside it and its rules are scoped to this page. Full guides at ",
                                A(Href: "https://github.com/pal-tamas/rask")["the Rask docs"],
                                "."
                            ]
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
