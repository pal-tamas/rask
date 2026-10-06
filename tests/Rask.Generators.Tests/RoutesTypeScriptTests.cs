using Rask.Generators.External;

namespace Rask.Generators.Tests;

/// <summary>
///     Covers the <c>@rask/routes</c> module: the C# <c>Routes</c> class as an island's front-end code sees it.
/// </summary>
/// <remarks>
///     What is asserted is the SHAPE — names, nesting, parameter keys, types and optionality. That the URLs it
///     formats are the ones C# formats is a different question, answered by running both sides
///     (<c>IslandRoutesTests</c> in Rask.External.Tests).
/// </remarks>
public class RoutesTypeScriptTests
{
    private const string Island = "public sealed partial class Chart : Rask.External.ReactComponent { }";

    [Fact]
    public void A_page_is_a_function_named_after_it_taking_one_object_keyed_by_the_csharp_parameter_names()
    {
        var typeScript = TypeScript(
            """
            using Rask.Core.Routing;
            namespace App;

            [Route("/users/{id:int}")]
            public sealed partial class UserPage : Rask.Core.Component
            {
                [RouteParam] public int Id { get; set; }
                [QueryParam] public string? Tab { get; set; }
            }
            """);

        Assert.Contains("UserPage: (p: { Id: number; Tab?: string | null }): Route =>", typeScript, StringComparison.Ordinal);
        Assert.Contains(
            "route(`/users/${url.segment(p.Id, format.number)}`, url.given('Tab', p.Tab, format.text)),",
            typeScript, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_with_no_parameters_takes_no_argument()
    {
        var typeScript = TypeScript(
            """
            using Rask.Core.Routing;
            namespace App;

            [Route("/")]
            public sealed partial class HomePage : Rask.Core.Component { }
            """);

        Assert.Contains("HomePage: (): Route =>", typeScript, StringComparison.Ordinal);
        Assert.Contains("route(``),", typeScript, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_whose_parameters_are_all_optional_can_be_called_with_none()
    {
        var typeScript = TypeScript(
            """
            using Rask.Core.Routing;
            namespace App;

            [Route("/files/{version?}")]
            public sealed partial class FilesPage : Rask.Core.Component
            {
                [RouteParam] public string? Version { get; set; }
                [QueryParam] public int Page { get; set; }
            }
            """);

        Assert.Contains(
            "FilesPage: (p: { Version?: string | null; Page?: number | null } = {}): Route =>",
            typeScript, StringComparison.Ordinal);

        // An optional segment brings its own slash, and a non-nullable query property is always written.
        Assert.Contains(
            "route(`/files${url.optional(p.Version, format.text)}`, url.always('Page', p.Page, format.number)),",
            typeScript, StringComparison.Ordinal);
    }

    [Fact]
    public void A_parameter_type_is_spelled_the_way_an_island_prop_of_that_type_is()
    {
        var typeScript = TypeScript(
            """
            using System;
            using Rask.Core.Routing;
            namespace App;

            public readonly record struct Slug(string Text) : IParsable<Slug>
            {
                public static Slug Parse(string s, IFormatProvider? p) => new(s);
                public static bool TryParse(string? s, IFormatProvider? p, out Slug result) { result = new(s ?? ""); return true; }
            }

            [Route("/orders/{id:guid}")]
            public sealed partial class OrderPage : Rask.Core.Component
            {
                [RouteParam] public Guid Id { get; set; }
                [QueryParam] public bool? Paid { get; set; }
                [QueryParam] public DateOnly? Day { get; set; }
                [QueryParam] public DateTime? Since { get; set; }
                [QueryParam("sort by")] public Slug? Order { get; set; }
            }
            """);

        Assert.Contains(
            "p: { Id: Guid; Paid?: boolean | null; Day?: DateOnly | null; Since?: Date | null; "
            + "Order?: string | null }",
            typeScript, StringComparison.Ordinal);

        // The aliases are declared here, so the module stands on its own; the query key is already encoded;
        // and a type only its own ToString() can write is a string the caller formats.
        Assert.Contains("export type DateOnly = string\nexport type Guid = string\n", typeScript, StringComparison.Ordinal);
        Assert.Contains("url.given('sort%20by', p.Order, format.text)", typeScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Pages_sharing_a_type_name_nest_under_their_folders_as_the_csharp_class_does()
    {
        var typeScript = TypeScript(
            """
            using Rask.Core.Routing;

            namespace App.Features.Admin { [Route("/admin")] public sealed partial class HomePage : Rask.Core.Component { } }
            namespace App.Features.Shop { [Route("/shop")] public sealed partial class HomePage : Rask.Core.Component { } }
            namespace App.Features.Shop { [Route("/cart")] public sealed partial class CartPage : Rask.Core.Component { } }
            """);

        var routes = typeScript[typeScript.IndexOf("export const Routes", StringComparison.Ordinal)..];

        Assert.Equal(
            """
            export const Routes = {
              CartPage: (): Route =>
                route(`/cart`),
              Admin: {
                HomePage: (): Route =>
                  route(`/admin`),
              },
              Shop: {
                HomePage: (): Route =>
                  route(`/shop`),
              },
            }

            """.ReplaceLineEndings("\n"),
            routes);
    }

    [Fact]
    public void A_project_with_islands_and_no_pages_gets_an_empty_Routes_and_a_working_Go()
    {
        var typeScript = TypeScript(string.Empty);

        Assert.Contains("export const Routes = {\n}\n", typeScript, StringComparison.Ordinal);
        Assert.Contains("export const Go = {", typeScript, StringComparison.Ordinal);
    }

    [Fact]
    public void There_is_no_way_to_navigate_to_a_path_typed_by_hand()
    {
        var typeScript = TypeScript(string.Empty);

        Assert.Contains("To: (route: Route): GoTo => route.Go(),", typeScript, StringComparison.Ordinal);
        Assert.Contains("readonly [generated]: true", typeScript, StringComparison.Ordinal);
        Assert.DoesNotContain("export function route", typeScript, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_without_islands_carries_no_routes_module()
    {
        var run = GeneratorDriverFixture.Run(
            [("/proj/Home.cs", "[Rask.Core.Routing.Route(\"/\")] public sealed partial class Home : Rask.Core.Component { }")],
            [new ExternalGenerator()]);

        var sources = run.RunResult.Results.SelectMany(r => r.GeneratedSources).Select(s => s.HintName);

        Assert.DoesNotContain("RaskExternalRoutes.g.cs", sources);
    }

    // Through the island generator, which is what carries the module out of the compiler.
    private static string TypeScript(string pages)
    {
        var run = GeneratorDriverFixture.Run([("/proj/App.cs", pages + "\n" + Island)], [new ExternalGenerator()]);
        var carrier = run.RunResult.Results.SelectMany(r => r.GeneratedSources)
            .Single(s => s.HintName == "RaskExternalRoutes.g.cs");

        Assert.Contains("public const string TypeScript = ", carrier.SourceText.ToString(), StringComparison.Ordinal);
        return RoutesGenerator.TypeScript(run.Compilation);
    }
}
