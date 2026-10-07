using System.Text.Json;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     What every committed front-end template tree must contain, asserted over the bytes a scaffolded app
///     receives.
/// </summary>
public sealed class TemplateTreeContractTests
{
    private static readonly string Root =
        Path.Combine(CliBuildE2E.FindRepoRoot(), "src", "Rask.Templates");

    public static TheoryData<string> SpaTemplates() => [.. SpaFramework.All.Select(f => f.Key)];

    // Rask.Spa.Hosting refuses a client with no tsconfig.json (RASKSPA004): the typed client is .ts.
    [Theory]
    [MemberData(nameof(SpaTemplates))]
    public void Every_front_end_client_is_written_in_TypeScript(string key)
    {
        var config = Path.Combine(Root, key, "client", "tsconfig.json");

        var exists = File.Exists(config);

        Assert.True(exists, $"{key}'s client has no tsconfig.json, so the host will refuse it with RASKSPA004.");
    }

    // src/rask is written on every build. Stored dot-less: a .gitignore inside a template is a rule for THIS repository.
    [Theory]
    [MemberData(nameof(SpaTemplates))]
    public void The_generated_client_is_not_committed(string key)
    {
        var ignore = Path.Combine(Root, key, "client", "gitignore");

        Assert.True(File.Exists(ignore), $"{key}'s client ships no gitignore.");
        Assert.Contains("src/rask/", File.ReadAllText(ignore), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(Root, key, "client", "src", "rask")));
    }

    [Theory]
    [MemberData(nameof(SpaTemplates))]
    public void The_client_manifest_is_valid_json_with_the_scripts_the_build_and_rask_dev_run(string key)
    {
        var manifest = Path.Combine(Root, key, "client", "package.json");

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));

        var scripts = document.RootElement.GetProperty("scripts");
        Assert.True(scripts.TryGetProperty("build", out _), $"{key}: `npm run build` is what the host's build runs.");
        // `rask dev` runs `dev` where there is one and `start` otherwise, which is what the Angular CLI writes.
        Assert.True(
            scripts.TryGetProperty("dev", out _) || scripts.TryGetProperty("start", out _),
            $"{key}: `npm run dev` (or `start`) is what `rask dev` runs.");
    }

    // Lower case, and the word the build looks in: a capital Client is the WASM lane's C# project.
    [Theory]
    [MemberData(nameof(SpaTemplates))]
    public void The_front_end_lives_in_a_lower_case_client_folder(string key)
    {
        var directories = Directory.GetDirectories(Path.Combine(Root, key)).Select(Path.GetFileName).ToArray();

        Assert.Contains("client", directories);
        Assert.DoesNotContain("Client", directories);
    }

    // Written as text, a PNG is re-encoded as UTF-8: the scaffold and the build succeed and the image is corrupt.
    [Fact]
    public async Task A_binary_asset_is_written_as_bytes()
    {
        var fs = new FakeFileSystem();
        var command = new NewCommand(new StringConsole(), fs, new FakeProcessRunner(), "/proj");

        var exit = await command.ExecuteAsync(
            ["Shop", "--template", "react", "--no-restore", "--no-git"], CancellationToken.None);

        Assert.Equal(0, exit);
        var png = fs.BinaryFiles.Keys.FirstOrDefault(p => p.EndsWith("hero.png", StringComparison.Ordinal));
        Assert.NotNull(png);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, fs.BinaryFiles[png][..4]);
        Assert.DoesNotContain(fs.Files.Keys, p => p.EndsWith("hero.png", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(SpaTemplates))]
    public void A_scaffolded_client_never_keeps_the_placeholder_name(string key)
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

    // CI's front-end jobs and their scoping go by the trees (run-template-e2e.sh --list-front-ends); `rask new`
    // goes by SpaFramework.All. A tree in only one of them is a template nothing gates, or a job with nothing to run.
    [Fact]
    public void The_trees_with_a_client_are_exactly_the_front_ends_rask_new_offers()
    {
        var offered = SpaFramework.All.Select(framework => framework.Key).Order(StringComparer.Ordinal);

        var committed = Directory.EnumerateDirectories(Root)
            .Where(tree => File.Exists(Path.Combine(tree, "client", "package.json")))
            .Select(tree => Path.GetFileName(tree))
            .Order(StringComparer.Ordinal);

        Assert.Equal(offered, committed);
    }
}
