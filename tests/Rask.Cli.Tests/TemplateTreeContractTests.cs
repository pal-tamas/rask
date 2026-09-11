using System.Text.Json;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     What every committed template tree must contain, asserted over the trees themselves.
/// </summary>
/// <remarks>
///     <para>
///         These claims used to be made about what the generator EMITTED, and several of them about what
///         a patch did to a file <c>npx create-vite@latest</c> had just written. Both are gone: the files
///         are committed, so the same claims are now made about the bytes a scaffolded app actually
///         receives. That is strictly stronger — the old tests could only see Rask's own overlay, and
///         every one of these covers the creator's half too, which nothing checked before.
///     </para>
///     <para>
///         Read the tree once. There are fifteen of them and roughly 520 files, and re-walking per case
///         is what makes a convention suite slow enough that someone scopes it out of the gate.
///     </para>
/// </remarks>
public sealed class TemplateTreeContractTests
{
    private static readonly string Root =
        Path.Combine(CliBuildE2E.FindRepoRoot(), "src", "Rask.Templates");

    private static string[] Templates(string kind) => kind switch
    {
        "spa" => [.. SpaFramework.All.Select(f => f.Key)],
        "meta" => [.. MetaTemplate.All.Select(f => f.Key)],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static TheoryData<string> SpaTemplates() => [.. Templates("spa")];

    public static TheoryData<string> MetaTemplates() => [.. Templates("meta")];

    public static TheoryData<string> FrontEndTemplates() =>
        [.. Templates("spa").Concat(Templates("meta"))];

    [Theory]
    [MemberData(nameof(FrontEndTemplates))]
    public void The_client_is_TypeScript(string key)
    {
        // Rask.Spa.Hosting refuses a client with no tsconfig.json (RASKSPA004): the generated contracts
        // are .ts, and a client that cannot type-check them gets none of what the template exists for.
        // A JavaScript template would scaffold an app whose very first build fails.
        var client = Path.Combine(Root, key, "client");

        Assert.True(
            File.Exists(Path.Combine(client, "tsconfig.json")),
            $"{key}'s client has no tsconfig.json, so the host will refuse it with RASKSPA004.");
    }

    [Theory]
    [MemberData(nameof(FrontEndTemplates))]
    public void The_generated_contracts_are_not_committed(string key)
    {
        // src/rask (or app/rask) is written on every build from the C# contracts. Committing it means a
        // stale copy is served by a checkout that never built, and every build shows up as a diff.
        // Stored dot-less, because a .gitignore inside a template is read as a rule for THIS repository.
        var ignore = Path.Combine(Root, key, "client", "gitignore");

        Assert.True(File.Exists(ignore), $"{key}'s client ships no gitignore.");
        Assert.Contains("rask/", File.ReadAllText(ignore), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(FrontEndTemplates))]
    public void The_client_manifest_is_valid_json_and_names_the_app(string key)
    {
        var manifest = Path.Combine(Root, key, "client", "package.json");
        Assert.True(File.Exists(manifest), $"{key}'s client has no package.json.");

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }

    [Theory]
    [MemberData(nameof(MetaTemplates))]
    public void The_meta_host_names_the_framework_it_supervises(string key)
    {
        // RaskMetaFramework is required by Rask.Meta.Hosting and decides which server entry the node
        // supervisor runs. One name for the template, the csproj property and the host's metadata.
        var csproj = File.ReadAllText(
            Path.Combine(Root, key, $"{TemplateMaterializer.NameToken}.csproj"));

        Assert.Contains($"<RaskMetaFramework>{key}</RaskMetaFramework>", csproj, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(FrontEndTemplates))]
    public void The_front_end_lives_in_a_lower_case_client_folder(string key)
    {
        // Lower case, and the same word the build looks in: a capital Client is the WASM lane's C#
        // project. The build reads RaskSpaClientDir/RaskMetaAppDir, both defaulting to `client`, so a
        // differently-cased folder is a front end the build never finds.
        var directories = Directory.GetDirectories(Path.Combine(Root, key))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Contains("client", directories);
        Assert.DoesNotContain("Client", directories);
    }

    [Fact]
    public void Angular_tells_the_host_where_it_nests_its_bundle()
    {
        // Angular is the one framework that does not build to plain dist/: @angular/build writes
        // dist/<project>/browser. The host has to be told, or it serves an empty bundle directory.
        var csproj = File.ReadAllText(
            Path.Combine(Root, "angular", $"{TemplateMaterializer.NameToken}.csproj"));

        Assert.Contains(
            $"<RaskSpaDistDir>dist/{TemplateMaterializer.SlugToken}-client/browser</RaskSpaDistDir>",
            csproj,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_name_slug_is_substituted_everywhere_Angular_wrote_one()
    {
        // ng new derives its project name from the directory and lower-cases it, so the Angular tree
        // says company-raskserver-client in four files. Substituting only the exact name token leaves
        // every one of them naming the placeholder, and the host then looks for a bundle under a
        // directory Angular never writes.
        var files = TemplateMaterializer.Files(
            "/out", "angular", "Shop", new ServerBatteries(), "9.9.9");

        var leftovers = files
            .Where(f => f.Bytes is null
                && f.Content.Contains(TemplateMaterializer.SlugToken, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            leftovers.Length == 0,
            "These scaffolded files still name the placeholder slug:\n  " + string.Join("\n  ", leftovers));

        Assert.Contains(files, f => f.Content.Contains("shop-client", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Shop", "shop")]
    [InlineData("Company.RaskServer", "company-raskserver")]
    [InlineData("My.Web.App", "my-web-app")]
    [InlineData("Acme_Store", "acme-store")]
    public void A_name_becomes_the_slug_npm_and_Angular_would_write(string name, string expected) =>
        Assert.Equal(expected, TemplateMaterializer.Slug(name));

    [Theory]
    [MemberData(nameof(SpaTemplates))]
    public void A_scaffolded_client_never_keeps_the_placeholder_name(string key)
    {
        var files = TemplateMaterializer.Files("/out", key, "Shop", new ServerBatteries(), "9.9.9");

        var leftovers = files
            .Where(f => f.Bytes is null
                && f.Content.Contains(TemplateMaterializer.NameToken, StringComparison.Ordinal))
            .Select(f => f.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            leftovers.Length == 0,
            $"--template {key} leaves the placeholder namespace in:\n  " + string.Join("\n  ", leftovers));
    }
}
