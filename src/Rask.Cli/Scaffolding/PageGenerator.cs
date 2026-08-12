namespace Rask.Cli.Scaffolding;

/// <summary>
/// Scaffolds a routed page <c>Component</c> under <c>Features/&lt;Feature&gt;/</c> (or an explicit output
/// dir). The class name always ends in <c>Page</c>; the feature name (that class minus the suffix) drives
/// the folder, the title, and the default route.
/// </summary>
internal static class PageGenerator
{
    public static ScaffoldFile Generate(
        ProjectContext project, string baseDirectory, string name, string? route, string? outputOverride)
    {
        // The class name always ends in exactly one "Page" — "Products" and "ProductsPage" both give
        // "ProductsPage" (never a doubled "PagePage"). The feature drives the folder, title, and route.
        var className = name.EndsWith("Page", StringComparison.Ordinal) ? name : name + "Page";
        var feature = FeatureNameOf(className);

        var targetDirectory = Scaffold.TargetDirectory(baseDirectory, outputOverride, "Features", feature);
        var routePath = NormalizeRoute(route) ?? Identifiers.ToRoutePath(feature);
        var content = Render(project.NamespaceFor(targetDirectory), className, routePath, feature);
        return new ScaffoldFile(Path.Combine(targetDirectory, className + ".cs"), content);
    }

    /// <summary>
    /// The feature name behind a page: "ProductsPage" → "Products". The degenerate "Page" (stripping
    /// would leave nothing) stays "Page".
    /// </summary>
    internal static string FeatureNameOf(string name)
    {
        if (name.EndsWith("Page", StringComparison.Ordinal) && name.Length > "Page".Length)
        {
            return name[..^"Page".Length];
        }

        return name;
    }

    /// <summary>Ensure a user-supplied route begins with '/'. Null/blank means "use the default".</summary>
    internal static string? NormalizeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return null;
        }

        return route[0] == '/' ? route : "/" + route;
    }

    /// <summary>Render the page source. Pure, so it is unit-tested directly.</summary>
    internal static string Render(string @namespace, string className, string route, string title) =>
        $$"""
        using Rask.Core.Routing;

        namespace {{@namespace}};

        [Route("{{route}}")]
        public sealed partial class {{className}} : Component
        {
            protected override Component? HeadAssets => Title["{{title}}"];

            protected override Component? Render() =>
            [
                H1["{{title}}"],
                P["A new page. Edit Render() to build it out."]
            ];
        }

        """;
}
