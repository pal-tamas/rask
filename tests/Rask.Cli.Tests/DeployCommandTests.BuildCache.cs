using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// On GitHub Actions the image is built on the runner's own BuildKit with the build cache the workflow keeps between
/// runs: the layer cache, and the cache mounts' contents carried in and out. Anything missing falls back to the plain
/// build, said in one line.
/// </summary>
public sealed partial class DeployCommandTests
{
    private const string RunnerTemp = "/runner/_temp";
    private const string ShopCache = RunnerTemp + "/rask-build-cache/shop";

    private const string MountingDockerfile = """
        FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
        RUN --mount=type=cache,id=rask-nuget,target=/root/.nuget/packages \
            dotnet restore
        RUN --mount=type=cache,id=rask-nuget,target=/root/.nuget/packages \
            --mount=type=cache,id=rask-chrome,target=/chrome,sharing=locked \
            --mount=type=bind,source=x,target=/x \
            dotnet publish
        """;

    private static Func<string, string?> OnRunner(string? temp = RunnerTemp) => name => name switch
    {
        "GITHUB_ACTIONS" => "true",
        "RUNNER_TEMP" => temp,
        _ => null,
    };

    private DeployCommand CreateOnRunner(FakeFileSystem fs, FakeProcessRunner runner, StringConsole console, Func<string, string?>? environment = null)
    {
        var command = CreateAt(fs, runner, console);
        fs.Seed("/proj/Dockerfile", MountingDockerfile);
        command.ReadEnvironment = environment ?? OnRunner();
        command.NewNonce = () => "n1";
        return command;
    }

    private static List<string[]> Docker(FakeProcessRunner runner) => [.. runner.Invocations.Select(i => i.Arguments.ToArray())];

    [Fact]
    public async Task A_deploy_on_github_actions_builds_with_the_cache_the_last_deploy_kept_and_keeps_its_own()
    {
        var fs = new FakeFileSystem();
        fs.Seed($"{ShopCache}/layers/index.json", "{}");
        fs.Seed($"{ShopCache}/mounts/rask-nuget/package.nupkg");
        var runner = new FakeProcessRunner
        {
            CaptureHandler = WithEngines("linux/amd64", "linux/amd64", args =>
            {
                if (args.Contains("type=local,dest=" + ShopCache + "/mounts-next"))
                {
                    fs.Seed($"{ShopCache}/mounts-next/rask-nuget/package.nupkg");
                    fs.Seed($"{ShopCache}/mounts-next/rask-chrome/chrome-headless-shell");
                }

                return Captures()(args);
            }),
        };
        var console = new StringConsole();
        var command = CreateOnRunner(fs, runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--port", "9000", "--app", "shop"], CancellationToken.None);

        Assert.Equal(0, exit);
        var docker = Docker(runner);
        var inbound = docker.FindIndex(a => a is ["buildx", "build", "--builder", "rask-deploy", "--build-arg", "RASK_CACHE_TRANSFER=n1", "--output", "type=cacheonly", ..]);
        var build = docker.FindIndex(a => a is ["buildx", "build", "--builder", "rask-deploy", "--platform", ..]);
        var outbound = docker.FindIndex(a => a.Contains($"type=local,dest={ShopCache}/mounts-next"));
        var save = docker.FindIndex(a => a is ["save", "shop:current"]);
        Assert.Equal(
            [
                "buildx", "build", "--builder", "rask-deploy", "--platform", "linux/amd64", "-t", "shop:current", "--label", "rask.release=v26.11.11.13.12.02", "--load",
                "--cache-from", $"type=local,src={ShopCache}/layers",
                "--cache-to", $"type=local,dest={ShopCache}/layers-next,mode=max,ignore-error=true",
                "-f", "/proj/Dockerfile", "/proj",
            ],
            docker[build]);
        Assert.Equal($"{ShopCache}/mounts", docker[inbound][^1]);
        Assert.True(inbound < build && build < outbound && outbound < save, string.Join("\n", docker.Select(a => string.Join(' ', a))));
        Assert.Contains("id=rask-nuget,target=/into/rask-nuget", fs.ReadAllText($"{ShopCache}/transfer/inbound.Dockerfile"), StringComparison.Ordinal);
        Assert.True(fs.FileExists($"{ShopCache}/mounts/rask-chrome/chrome-headless-shell"));
        Assert.False(fs.DirectoryExists($"{ShopCache}/mounts-next"));
        Assert.Contains("restored by the workflow", console.OutText, StringComparison.Ordinal);
        Assert.Contains("Kept the build cache for the next deploy: its layers and rask-nuget, rask-chrome.", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cache_restored_for_the_same_project_files_is_read_but_not_written_again()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/Shop.csproj", "<Project/>");
        fs.Seed($"{ShopCache}/layers/index.json", "{}");
        fs.Seed($"{ShopCache}/mounts/rask-nuget/package.nupkg");
        var runner = new FakeProcessRunner { CaptureHandler = WithEngines("linux/amd64", "linux/amd64") };
        var console = new StringConsole();
        var command = CreateOnRunner(fs, runner, console);
        fs.Seed($"{ShopCache}/fingerprint", BuildInputs.Fingerprint(fs, "/proj", MountingDockerfile));

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--port", "9000", "--app", "shop"], CancellationToken.None);

        Assert.Equal(0, exit);
        var docker = Docker(runner);
        var build = docker.Single(a => a is ["buildx", "build", "--builder", "rask-deploy", "--platform", ..]);
        Assert.Contains($"type=local,src={ShopCache}/layers", build);
        Assert.DoesNotContain("--cache-to", build);
        Assert.Contains(docker, a => a.Contains("type=cacheonly"));
        Assert.DoesNotContain(docker, a => a.Contains($"type=local,dest={ShopCache}/mounts-next"));
        Assert.True(fs.FileExists($"{ShopCache}/mounts/rask-nuget/package.nupkg"));
        Assert.Contains("for these same project files", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cache_restored_for_other_project_files_is_written_again_with_the_new_fingerprint()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/Shop.csproj", "<Project/>");
        fs.Seed($"{ShopCache}/layers/index.json", "{}");
        fs.Seed($"{ShopCache}/fingerprint", "an-older-build");
        var runner = new FakeProcessRunner { CaptureHandler = WithEngines("linux/amd64", "linux/amd64") };
        var command = CreateOnRunner(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--port", "9000", "--app", "shop"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(Docker(runner), a => a.Contains("--cache-to"));
        Assert.Contains(Docker(runner), a => a.Contains($"type=local,dest={ShopCache}/mounts-next"));
        Assert.Equal(BuildInputs.Fingerprint(fs, "/proj", MountingDockerfile), fs.ReadAllText($"{ShopCache}/fingerprint"));
    }

    [Fact]
    public void The_fingerprint_moves_with_the_restore_inputs_and_the_dockerfile_but_not_with_the_code()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/Shop.csproj", "<Project/>");
        fs.Seed("/proj/Directory.Build.props", "<Project/>");
        fs.Seed("/proj/Program.cs", "one");
        var before = BuildInputs.Fingerprint(fs, "/proj", "FROM a");

        fs.Seed("/proj/Program.cs", "two");
        var codeEdited = BuildInputs.Fingerprint(fs, "/proj", "FROM a");
        fs.Seed("/proj/NuGet.Config", "<configuration/>");
        var feedAdded = BuildInputs.Fingerprint(fs, "/proj", "FROM a");
        var dockerfileEdited = BuildInputs.Fingerprint(fs, "/proj", "FROM b");

        Assert.Equal(before, codeEdited);
        Assert.NotEqual(before, feedAdded);
        Assert.NotEqual(feedAdded, dockerfileEdited);
    }

    [Fact]
    public async Task A_first_deploy_on_github_actions_reads_no_cache_and_writes_one()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = WithEngines("linux/amd64", "linux/amd64") };
        var console = new StringConsole();
        var command = CreateOnRunner(fs, runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--port", "9000", "--app", "shop"], CancellationToken.None);

        Assert.Equal(0, exit);
        var docker = Docker(runner);
        var build = docker.Single(a => a is ["buildx", "build", "--builder", "rask-deploy", "--platform", ..]);
        Assert.DoesNotContain("--cache-from", build);
        Assert.Contains($"type=local,dest={ShopCache}/layers-next,mode=max,ignore-error=true", build);
        Assert.DoesNotContain(docker, a => a.Contains("type=cacheonly"));
        Assert.Contains(docker, a => a.Contains($"type=local,dest={ShopCache}/mounts-next"));
        Assert.Contains("none yet", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_runner_that_cannot_start_a_buildkit_builder_builds_the_plain_way_and_says_why()
    {
        var runner = new FakeProcessRunner
        {
            CaptureHandler = WithEngines("linux/amd64", "linux/amd64", args =>
                args is ["buildx", "inspect", "rask-deploy"] ? new ProcessResult(1, string.Empty, "ERROR: no builder \"rask-deploy\" found")
                : args is ["buildx", "create", ..] ? new ProcessResult(1, string.Empty, "ERROR: docker-container driver needs a Docker engine")
                : Captures()(args)),
        };
        var console = new StringConsole();
        var command = CreateOnRunner(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--port", "9000", "--app", "shop"], CancellationToken.None);

        Assert.Equal(0, exit);
        var docker = Docker(runner);
        Assert.Contains(docker, a => a is ["build", "--platform", "linux/amd64", "-t", "shop:current", ..]);
        Assert.DoesNotContain(docker, a => a is ["buildx", "build", ..]);
        Assert.Contains("Could not start a BuildKit builder on this runner (ERROR: docker-container driver needs a Docker engine)", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_builder_already_running_in_the_job_is_reused()
    {
        var runner = new FakeProcessRunner { CaptureHandler = WithEngines("linux/amd64", "linux/amd64") };
        var command = CreateOnRunner(new FakeFileSystem(), runner, new StringConsole());

        await command.ExecuteAsync(["--host", "deploy@box", "--port", "9000", "--app", "shop"], CancellationToken.None);

        Assert.Contains(Docker(runner), a => a is ["buildx", "inspect", "rask-deploy"]);
        Assert.DoesNotContain(Docker(runner), a => a is ["buildx", "create", ..]);
    }

    [Fact]
    public async Task Cache_mounts_that_cannot_be_copied_out_never_fail_the_deploy()
    {
        var fs = new FakeFileSystem();
        fs.Seed($"{ShopCache}/mounts/rask-nuget/package.nupkg");
        var runner = new FakeProcessRunner
        {
            CaptureHandler = WithEngines("linux/amd64", "linux/amd64", args =>
                args.Contains($"type=local,dest={ShopCache}/mounts-next") ? new ProcessResult(1, string.Empty, "ERROR: no space left on device") : Captures()(args)),
        };
        var console = new StringConsole();
        var command = CreateOnRunner(fs, runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--port", "9000", "--app", "shop"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("Could not keep the build's cache mounts (ERROR: no space left on device)", console.OutText, StringComparison.Ordinal);
        Assert.True(fs.FileExists($"{ShopCache}/mounts/rask-nuget/package.nupkg"));
        Assert.Contains(Docker(runner), a => a is ["-H", "ssh://deploy@box", "load"]);
    }

    [Theory]
    [InlineData(null, RunnerTemp)]
    [InlineData("false", RunnerTemp)]
    [InlineData("true", null)]
    public async Task Outside_a_github_actions_run_the_build_is_the_plain_one(string? actions, string? temp)
    {
        var runner = new FakeProcessRunner { CaptureHandler = WithEngines("linux/amd64", "linux/amd64") };
        var command = CreateOnRunner(new FakeFileSystem(), runner, new StringConsole(), name => name switch
        {
            "GITHUB_ACTIONS" => actions,
            "RUNNER_TEMP" => temp,
            _ => null,
        });

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--port", "9000", "--app", "shop"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(Docker(runner), a => a.Contains("buildx", StringComparer.Ordinal) && !a.Contains("version", StringComparer.Ordinal));
        Assert.Contains(Docker(runner), a => a is ["build", "--platform", "linux/amd64", ..]);
    }

    [Fact]
    public void The_cache_mounts_carried_are_every_cache_id_in_the_dockerfile_once()
    {
        const string dockerfile = """
            RUN --mount=type=cache,id=rask-nuget,target=/root/.nuget/packages dotnet restore
            RUN --mount=type=cache,id=rask-nuget,target=/root/.nuget/packages \
                --mount=type=cache,target=/var/cache/apt \
                --mount=type=cache,target=apt-lists \
                --mount=type=secret,id=token \
                --mount=type=cache,id=rask-chrome,target=/chrome,sharing=locked \
                dotnet publish
            """;

        var ids = RunnerBuildCache.CacheMountIds(dockerfile);

        Assert.Equal(["rask-nuget", "apt-lists", "rask-chrome"], ids);
    }

    [Fact]
    public void The_transfer_dockerfiles_copy_each_mount_through_a_folder_of_its_id()
    {
        string[] ids = ["rask-nuget", "rask-chrome"];

        var inbound = CacheMountTransfer.Inbound(ids);
        var outbound = CacheMountTransfer.Outbound(ids);

        Assert.Equal(
            "FROM busybox:1\n"
            + "ARG RASK_CACHE_TRANSFER\n"
            + "RUN --mount=type=bind,target=/from \\\n"
            + "    --mount=type=cache,id=rask-nuget,target=/into/rask-nuget \\\n"
            + "    --mount=type=cache,id=rask-chrome,target=/into/rask-chrome \\\n"
            + "    echo \"$RASK_CACHE_TRANSFER\" && for id in rask-nuget rask-chrome; do if [ -d \"/from/$id\" ]; then cp -a \"/from/$id/.\" \"/into/$id/\"; fi; done\n",
            inbound);
        Assert.Equal(
            "FROM busybox:1 AS mounts\n"
            + "ARG RASK_CACHE_TRANSFER\n"
            + "RUN --mount=type=cache,id=rask-nuget,target=/from/rask-nuget \\\n"
            + "    --mount=type=cache,id=rask-chrome,target=/from/rask-chrome \\\n"
            + "    echo \"$RASK_CACHE_TRANSFER\" && mkdir /into && for id in rask-nuget rask-chrome; do cp -a \"/from/$id\" \"/into/$id\"; done\n"
            + "FROM scratch\n"
            + "COPY --from=mounts /into /\n",
            outbound);
    }

    [Fact]
    public void The_cache_copies_are_never_built_with_no_cache_which_would_give_them_empty_mounts_of_their_own()
    {
        var cache = new RunnerBuildCache(ShopCache, ["rask-nuget"]);

        var inbound = DockerCommands.BuildMountsInArguments(cache, "a");
        var outbound = DockerCommands.BuildMountsOutArguments(cache, "b");

        Assert.DoesNotContain("--no-cache", inbound);
        Assert.DoesNotContain("--no-cache", outbound);
        Assert.Contains("RASK_CACHE_TRANSFER=a", inbound);
        Assert.Contains("RASK_CACHE_TRANSFER=b", outbound);
    }

    [Fact]
    public void A_runner_build_and_its_cache_copies_get_a_builds_deadline()
    {
        var deadlines = DeployDeadlines.Default;

        var build = deadlines.For(["buildx", "build", "--builder", "rask-deploy", "."]);
        var inspect = deadlines.For(["buildx", "inspect", "rask-deploy"]);

        Assert.Equal(deadlines.Build, build);
        Assert.Equal(deadlines.Step, inspect);
    }

    [Fact]
    public void The_workflow_keeps_the_folder_the_deploy_writes_its_build_cache_to()
    {
        var workflow = GitHubActionsWorkflow.Render(ciWorkflow: null);

        var cache = workflow.IndexOf("uses: actions/cache@v6", StringComparison.Ordinal);
        var deploy = workflow.IndexOf("run: rask deploy --no-setup-host", StringComparison.Ordinal);

        Assert.True(cache > 0 && cache < deploy, workflow);
        Assert.Contains($"path: ${{{{ runner.temp }}}}/{RunnerBuildCache.Root}\n", workflow, StringComparison.Ordinal);
        Assert.Contains("key: rask-build-${{ runner.os }}-${{ runner.arch }}-${{ hashFiles('**/*.csproj', '**/Directory.*.props', '**/global.json', '**/[Nn]u[Gg]et.[Cc]onfig', '**/Dockerfile') }}", workflow, StringComparison.Ordinal);
        Assert.Contains("rask-build-${{ runner.os }}-${{ runner.arch }}-\n", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ACTIONS_RUNTIME_TOKEN", workflow, StringComparison.Ordinal);
    }
}
