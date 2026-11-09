namespace Rask.Cli.Tests;

/// <summary>
///     A template image's build stage downloads its packages and Rask.Pdf's browser once, not on every deploy:
///     each download runs under a BuildKit cache mount, which <c>COPY . .</c> does not invalidate. Every
///     <c>rask deploy</c> builds the image anew, so a step outside a mount costs its download every time.
/// </summary>
public sealed class TemplateBuildCacheTests
{
    private const string NuGetCache = "--mount=type=cache,id=rask-nuget,target=/root/.nuget/packages";
    private const string TailwindCache = "--mount=type=cache,id=rask-tailwind,target=/root/.rask/tailwind";
    private const string TypeScriptCache = "--mount=type=cache,id=rask-typescript,target=/root/.rask/typescript";
    private const string ChromeCache = "--mount=type=cache,id=rask-chrome,target=/chrome,sharing=locked";

    private static readonly string Root = Path.Combine(CliBuildE2E.FindRepoRoot(), "src", "Rask.Templates");

    [Fact]
    public void Every_template_image_restores_and_publishes_under_a_cache_mount()
    {
        var images = Dockerfiles();

        var uncached = images
            .SelectMany(file => Runs(file)
                .Where(run => (run.Contains("dotnet restore", StringComparison.Ordinal) || run.Contains("dotnet publish", StringComparison.Ordinal))
                    && !run.Contains(NuGetCache, StringComparison.Ordinal))
                .Select(run => $"{Path.GetRelativePath(Root, file)}: {run}"))
            .ToList();

        Assert.NotEmpty(images);
        Assert.True(uncached.Count == 0, "These steps download NuGet packages on every build:\n  " + string.Join("\n  ", uncached));
    }

    [Fact]
    public void Every_template_publish_compiles_its_stylesheet_with_the_cached_tailwind_cli()
    {
        var images = Dockerfiles();

        var uncached = images
            .SelectMany(file => Runs(file)
                .Where(run => run.Contains("dotnet publish", StringComparison.Ordinal) && !run.Contains(TailwindCache, StringComparison.Ordinal))
                .Select(run => $"{Path.GetRelativePath(Root, file)}: {run}"))
            .ToList();

        Assert.NotEmpty(images);
        Assert.True(uncached.Count == 0, "These steps download the Tailwind CLI on every build:\n  " + string.Join("\n  ", uncached));
    }

    [Fact]
    public void Every_template_publish_compiles_its_typescript_with_the_cached_tsgo()
    {
        var images = Dockerfiles();

        var uncached = images
            .SelectMany(file => Runs(file)
                .Where(run => run.Contains("dotnet publish", StringComparison.Ordinal) && !run.Contains(TypeScriptCache, StringComparison.Ordinal))
                .Select(run => $"{Path.GetRelativePath(Root, file)}: {run}"))
            .ToList();

        Assert.NotEmpty(images);
        Assert.True(uncached.Count == 0, "These steps download tsgo on every build:\n  " + string.Join("\n  ", uncached));
    }

    [Fact]
    public void Every_aspnet_template_image_copies_the_app_after_the_layers_a_code_edit_cannot_change()
    {
        var images = Dockerfiles().Where(file => File.ReadAllText(file).Contains("mcr.microsoft.com/dotnet/aspnet", StringComparison.Ordinal)).ToList();

        var early = images
            .Where(file => File.ReadAllText(file) is var text
                && text.IndexOf("COPY --from=build /app .", StringComparison.Ordinal) < text.IndexOf("apt-get install", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(Root, file))
            .ToList();

        Assert.NotEmpty(images);
        Assert.True(early.Count == 0, "These images install the PDF browser's libraries again on every code edit:\n  " + string.Join("\n  ", early));
    }

    [Fact]
    public void No_template_image_installs_node_or_runs_npm()
    {
        var images = Dockerfiles();

        var node = images
            .SelectMany(file => Runs(file)
                .Where(run => run.Contains("npm ", StringComparison.Ordinal) || run.Contains("nodejs", StringComparison.Ordinal)
                    || run.Contains("nodesource", StringComparison.Ordinal))
                .Select(run => $"{Path.GetRelativePath(Root, file)}: {run}"))
            .ToList();

        Assert.NotEmpty(images);
        Assert.True(node.Count == 0, "These steps need Node in the image:\n  " + string.Join("\n  ", node));
    }

    [Fact]
    public void The_pdf_shell_is_fetched_into_a_locked_cache_and_copied_out_in_the_same_step()
    {
        var images = Dockerfiles().Where(file => File.ReadAllText(file).Contains("/opt/rask/chrome", StringComparison.Ordinal)).ToList();

        var wrong = images
            .Where(file => !Runs(file).Any(run => run.Contains(ChromeCache, StringComparison.Ordinal)
                && run.Contains("-p:RaskPdfChromiumCache=/chrome", StringComparison.Ordinal)
                && run.Contains("-p:RaskPdfChromiumCopyTo=/chrome-shell", StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(Root, file))
            .ToList();

        Assert.NotEmpty(images);
        Assert.True(wrong.Count == 0, "These images fetch the PDF shell on every build, or lose it with the mount:\n  " + string.Join("\n  ", wrong));
    }

    [Fact]
    public void Every_template_image_names_the_dockerfile_syntax_its_cache_mounts_need()
    {
        var images = Dockerfiles();

        var unnamed = images
            .Where(file => !File.ReadAllText(file).StartsWith("# syntax=docker/dockerfile:1\n", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(Root, file))
            .ToList();

        Assert.NotEmpty(images);
        Assert.True(unnamed.Count == 0, "These images do not start with # syntax=docker/dockerfile:1:\n  " + string.Join("\n  ", unnamed));
    }

    private static List<string> Dockerfiles() =>
        [.. Directory.EnumerateFiles(Root, "Dockerfile", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

    /// <summary>Each RUN instruction with its continuation lines joined, so a flag on any line of it counts.</summary>
    private static IEnumerable<string> Runs(string dockerfile)
    {
        var instruction = new List<string>();
        foreach (var line in File.ReadAllLines(dockerfile))
        {
            if (instruction.Count == 0 && !line.StartsWith("RUN ", StringComparison.Ordinal))
            {
                continue;
            }

            instruction.Add(line.TrimEnd('\\').Trim());
            if (!line.EndsWith('\\'))
            {
                yield return string.Join(' ', instruction);
                instruction.Clear();
            }
        }
    }
}
