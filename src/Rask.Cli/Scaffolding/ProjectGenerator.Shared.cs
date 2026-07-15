namespace Rask.Cli.Scaffolding;

// Template content shared by more than one template, emitted verbatim with the Company.RaskServer
// namespace token replaced centrally (see ProjectGenerator.Materialize).
internal static partial class ProjectGenerator
{
    private const string HomePage =
        """
        using Rask.Core.Routing;

        namespace Company.RaskServer;

        [Route("/")]
        public sealed class HomePage : Component
        {
            protected override Component? Render() =>
                Div(Class: "welcome-card")[
                    H1(Class: "welcome-title")["Hello, Rask! 👋"],
                    P(Class: "welcome-lead")["Your app is ready. Scaffold the rest with the rask CLI:"],
                    Ul(Class: "welcome-cheatsheet")[
                        Li()[Code()["rask generate feature Product Name:string Price:decimal"], " — a full CRUD slice (entity, pages, tests)"],
                        Li()[Code()["rask generate page About"], " — a routed page"],
                        Li()[Code()["rask generate component Card"], " — a reusable component"],
                        Li()[Code()["rask dev"], " — run with hot reload"]
                    ],
                    P(Class: "welcome-hint")[
                        "Edit this page in ",
                        Code()["HomePage.cs"],
                        " — styled by the auto-scoped ",
                        Code()["HomePage.css"],
                        ". Full guides at ",
                        A(Href: "https://github.com/pal-tamas/rask")["the Rask docs"],
                        "."
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

        .welcome-cheatsheet {
            margin: 0 0 1rem;
            padding-left: 1.1rem;
            font-size: 0.95rem;
            line-height: 1.75;
            color: #374151;
        }

        .welcome-hint {
            margin: 0;
            font-size: 0.9rem;
            color: #6b7280;
        }

        .welcome-card code {
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
