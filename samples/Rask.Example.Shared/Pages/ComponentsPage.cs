using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("components")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ComponentsPage : Component
{
    protected override RenderResult Head => Title()["User components — Rask"];

    protected override RenderResult Render() =>
        [
            PageHeader.Render(
                "User components",
                "Subclass Component, override Render. The Rask source generator emits a Namespace.Generated.TypeName(...) factory for every concrete user component, with parameters derived from your public settable properties."),
            H2(Class: "h4 mt-4 mb-3")["A component and its generated factory"],
            CodeSample(
                """
                public sealed class Greeting : Component
                {
                    public required string Name { get; set; }
                    public string? Title { get; set; }

                    public override RenderResult Render() =>
                        P()[
                            Title is null ? "" : $"{Title} ",
                            "Hello, ", Strong()[Name], "!"
                        ];
                }

                // call site (generated factory):
                Generated.Greeting(Name: "Ada", Title: "Dr.")
                """,
                Notes:
                "Non-nullable property without an initializer → required factory parameter. Nullable property → optional with default null. Property with an initializer → excluded from the factory.",
                Result: Greeting("Ada", "Dr.")),
            H2(Class: "h4 mt-5 mb-3")["DI via constructor"],
            CodeSample(
                """
                // Inject services like HttpClient/Navigator/RouteState through the
                // primary constructor — never as a public settable property:
                public sealed class WeatherCard(HttpClient http) : Component
                {
                    public required string City { get; set; }
                    // ... uses `http` to fetch
                }

                // call site is unchanged — ActivatorUtilities resolves `http`:
                Generated.WeatherCard(City: "Helsinki")
                """,
                Notes:
                "ActivatorUtilities.CreateInstance constructs the component each time; constructor parameters resolve from DI, properties are then re-applied so cached private state survives across renders while props stay fresh."),
            H2(Class: "h4 mt-5 mb-3")["[SkipFactory] hides a property"],
            CodeSample(
                """
                public sealed class SkipFactoryCounter : Component
                {
                    private int _count;

                    // [SkipFactory] excludes this property from the generated factory.
                    // The initializer seeds the cached instance — the factory call site
                    // doesn't have to (and can't) pass Initial through.
                    [SkipFactory] public int Initial { get; set; } = 7;

                    protected override void OnMount() => _count = Initial;

                    protected override RenderResult Render() =>
                        Button(OnClick: () => _count++)[$"Clicks: {_count}"];
                }

                // The generated factory has NO Initial parameter — the call site stays
                // clean. Framework caches the instance by tree position, so _count
                // survives re-renders just like any other private state.
                SkipFactoryCounter()
                """,
                Notes:
                "[SkipFactory] keeps a property settable in code while removing it from the generated factory signature. The counter below started at 7 — click it and the state persists across re-renders.",
                Result: SkipFactoryCounter()),
            H2(Class: "h4 mt-5 mb-3")["Diagnostics"],
            Div(Class: "list-group mb-3")[
                Div(Class: "list-group-item d-flex align-items-start")[
                    Span(Class: "badge text-bg-secondary me-3")["RASK001"],
                    Div()[
                        Strong()["Hidden suggestion."],
                        " A property is treated as a required factory parameter — consider also marking it ",
                        Code()["required"],
                        " for language-level enforcement."
                    ]
                ],
                Div(Class: "list-group-item d-flex align-items-start")[
                    Span(Class: "badge text-bg-warning me-3")["RASK002"],
                    Div()[
                        Strong()["Warning."],
                        " ", Code()["required"],
                        " on a property combined with DI-injected constructor parameters: ",
                        Code()["ActivatorUtilities"],
                        " cannot satisfy ", Code()["required"], " members. Drop one or the other."
                    ]
                ]
            ]
        ];
}

public sealed class SkipFactoryCounter : Component
{
    private int _count;

    [SkipFactory] public int Initial { get; set; } = 7;

    protected override void OnMount() => _count = Initial;

    protected override RenderResult Render() =>
        Button(
            Class: "btn btn-outline-primary",
            Id: "skipfactory-counter",
            OnClick: () => _count++)[I(Class: "bi bi-hand-index me-2"), $"Clicks: {_count}"];
}

public sealed class Greeting : Component
{
    public required string Name { get; set; }
    public string? Title { get; set; }

    protected override RenderResult Render() =>
        P(Class: "mb-0")[
            Title is null ? "" : $"{Title} ",
            "Hello, ", Strong()[Name], "!"
        ];
}
