namespace Rask.Cli.Scaffolding;

/// <summary>
/// Scaffolds a plain Rask <c>Component</c> under <c>Features/Shared/</c> — or into a feature slice
/// <c>Features/&lt;Feature&gt;/</c> when <c>--feature</c> names one (or an explicit <c>--output</c> dir).
/// </summary>
internal static class ComponentGenerator
{
    public static ScaffoldFile Generate(
        ProjectContext project, string baseDirectory, string name, string? feature, string? outputOverride)
    {
        var targetDirectory = Scaffold.TargetDirectory(baseDirectory, outputOverride, Scaffold.FeatureOrShared(feature));
        var content = Render(project.NamespaceFor(targetDirectory), name);
        return new ScaffoldFile(Path.Combine(targetDirectory, name + ".cs"), content);
    }

    /// <summary>Render the component source. Pure, so it is unit-tested directly.</summary>
    internal static string Render(string @namespace, string name) =>
        $$"""
        namespace {{@namespace}};

        public sealed class {{name}} : Component
        {
            protected override Component? Render() =>
            [
                Div()["{{name}} works. Edit Render() to build it out."]
            ];
        }

        """;
}
