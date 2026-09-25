using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     What a scaffolded app keeps out of git and out of its image: the production env file the docs tell you to
///     write, databases and their WAL, and the archives <c>rask db backup</c> leaves in the working directory.
/// </summary>
/// <remarks>
///     <c>git add .</c> or the Dockerfile's <c>COPY . .</c> would otherwise carry real secrets and real users' data
///     into a commit or an image layer, and <c>rask deploy</c> ships the whole build context to the remote daemon.
/// </remarks>
public sealed class ScaffoldIgnoresSecretsTests
{
    private const string Root = "/tmp/app";

    public static TheoryData<string> Apps() => ["server", "wasm", "wasm-hosted", "react"];

    [Theory]
    [MemberData(nameof(Apps))]
    public void Git_ignores_every_env_file_but_the_example_and_every_backup_archive(string app)
    {
        var gitignore = Generate(app)[".gitignore"].Split('\n');

        Assert.Contains(".env.*", gitignore);
        Assert.Contains("!.env.example", gitignore);
        Assert.Contains("*.tgz", gitignore);
    }

    [Theory]
    [MemberData(nameof(Apps))]
    public void The_image_build_context_carries_no_env_file_database_or_backup(string app)
    {
        var dockerignore = Generate(app)[".dockerignore"].Split('\n');

        Assert.Contains(".env", dockerignore);
        Assert.Contains(".env.*", dockerignore);
        Assert.Contains("*.db", dockerignore);
        Assert.Contains("*.tgz", dockerignore);
        Assert.Contains("storage/", dockerignore);
    }

    private static Dictionary<string, string> Generate(string app)
    {
        var result = app switch
        {
            "server" => ProjectGenerator.GenerateServer(Root, "Shop", (NewCommand.BatteriesOf([]) with { Docker = true }), "1.2.3"),
            "wasm" => ProjectGenerator.GenerateWasm(Root, "Shop", pwa: false, docker: true, "1.2.3"),
            "wasm-hosted" => ProjectGenerator.GenerateWasmHosted(Root, "Shop", (NewCommand.BatteriesOf([]) with { Docker = true }), "1.2.3"),
            _ => ProjectGenerator.GenerateSpa(Root, "Shop", SpaFramework.React, new ServerBatteries { Docker = true }, "1.2.3"),
        };

        return result.Files.ToDictionary(
            f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
            f => f.Content.ReplaceLineEndings("\n"));
    }
}
