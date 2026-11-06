using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

/// <summary>
///     What every committed template tree must hold, asserted over the bytes a scaffolded app receives.
/// </summary>
public sealed class TemplateTreeContractTests
{
    private static readonly string Root =
        Path.Combine(CliBuildE2E.FindRepoRoot(), "src", "Rask.Templates");

    public static TheoryData<string> Templates() => [.. TemplateCatalog.Keys];

    [Theory]
    [MemberData(nameof(Templates))]
    public void A_scaffolded_app_never_keeps_the_placeholder_name(string key)
    {
        var files = TemplateMaterializer.Files("/out", key, "Shop", new ServerBatteries(), "9.9.9");

        var leftovers = files
            .Where(f => f.Bytes is null
                && (f.Content.Contains(TemplateMaterializer.NameToken, StringComparison.Ordinal)
                    || f.Content.Contains(TemplateMaterializer.VersionToken, StringComparison.Ordinal)))
            .Select(f => f.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            leftovers.Length == 0,
            $"--template {key} leaves a placeholder in:\n  " + string.Join("\n  ", leftovers));
    }

    // A Rask app builds with the .NET SDK alone: a package.json anywhere in a tree would ask its user for Node.
    [Fact]
    public void No_template_tree_carries_an_npm_manifest()
    {
        var manifests = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
            .Where(path => Path.GetFileName(path) is "package.json" or "package-lock.json" or ".node-version" or ".nvmrc")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(manifests.Length == 0, "These template files need Node:\n  " + string.Join("\n  ", manifests));
    }

    // The trees on disk are exactly the templates `rask new` offers, plus the shared `_vscode*` fragments.
    [Fact]
    public void The_committed_trees_are_exactly_the_templates_rask_new_offers()
    {
        var committed = Directory.EnumerateDirectories(Root)
            .Select(tree => Path.GetFileName(tree))
            .Where(name => !name.StartsWith('_'))
            .Order(StringComparer.Ordinal);

        Assert.Equal(TemplateCatalog.Keys.Order(StringComparer.Ordinal), committed);
    }
}
