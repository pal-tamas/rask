namespace Rask.Cli.Scaffolding;

// Template content shared by more than one template, emitted verbatim with the Company.RaskServer
// namespace token replaced centrally (see ProjectGenerator.Materialize).
internal static partial class ProjectGenerator
{
    // The whole app surface a new project gets: the shell (which every page renders through, RASK021) and a
    // welcome home page that teaches the CLI. Both live in App.cs — a new project is deliberately one file of
    // components, not a folder of demos to delete. Styled with Bootstrap so there is no scoped .css to pair.
    private const string AppCs =
        """
        using Rask.Core.Routing;

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
                        Body()[Router()]
                    ]
                ];
        }

        [Route("/")]
        public sealed class HomePage : Component
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
                                Code()["App.cs"],
                                ". Full guides at ",
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
