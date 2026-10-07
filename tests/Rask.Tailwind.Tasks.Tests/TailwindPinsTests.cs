using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Rask.Tailwind.Tasks.Tests;

// #1182: the release's own sha256sums.txt comes from the same place as the binary, so it cannot say whether
// the asset was replaced. The digests are recorded in the repository instead — which only helps while the
// pinned version has one for every asset, and while a download that does not match is a failure rather
// than a reason to quietly use npm.
public class TailwindPinsTests
{
    [Fact]
    public void The_pinned_version_has_a_recorded_digest_for_every_asset()
    {
        var pinned = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "Rask.Tailwind", "build", "Rask.Tailwind.props"))
            .Descendants()
            .Single(e => e.Name.LocalName == "RaskTailwindVersion")
            .Value;
        var assets =
            from os in Enum.GetValues<TailwindOs>()
            from architecture in new[] { Architecture.X64, Architecture.Arm64 }
            from musl in new[] { false, true }
            let asset = TailwindCli.AssetName(os, architecture, musl)
            where asset is not null
            select asset;

        var unpinned = assets.Distinct().Where(asset => TailwindPins.For(pinned, asset) is null).ToArray();

        Assert.True(
            unpinned.Length == 0,
            $"RaskTailwindVersion is {pinned} but src/Rask.Tailwind.Tasks/TailwindPins.cs records no digest for: "
            + string.Join(", ", unpinned) + ". Replace the table from that release's sha256sums.txt.");
    }

    [Fact]
    public void A_version_Rask_does_not_pin_has_no_recorded_digest()
    {
        var digest = TailwindPins.For("0.0.1", "tailwindcss-linux-x64");

        Assert.Null(digest);
    }

    [Fact]
    public void A_binary_that_is_not_the_expected_one_fails_the_build_instead_of_falling_back_to_npm()
    {
        var cache = Path.Combine(Path.GetTempPath(), "rask-tailwind-swapped-" + Guid.NewGuid().ToString("n"));
        var engine = new RecordingEngine();
        var task = new ResolveTailwindCliTask
        {
            BuildEngine = engine,
            Version = TailwindPins.Version,
            CacheRoot = cache,
            ExpectedSha256 = new string('0', 64),
        };

        var resolved = task.Execute();

        // A platform with no standalone binary never downloads one, so there is nothing to refuse.
        if (!engine.Messages.Exists(m => m.Contains("fetching", StringComparison.Ordinal)))
        {
            return;
        }

        Assert.False(resolved);
        Assert.False(task.UseNpm);
        Assert.Contains("not the expected", Assert.Single(engine.Errors), StringComparison.Ordinal);
        Assert.False(Directory.Exists(cache) && Directory.GetFiles(cache, "*", SearchOption.AllDirectories).Length > 0);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rask.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
