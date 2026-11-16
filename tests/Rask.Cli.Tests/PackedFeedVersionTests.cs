namespace Rask.Cli.Tests;

/// <summary>
///     The build gates find each package in the feed they packed — on a stable tag too, where Rask.Postgres is held as
///     a prerelease of that version (v0.24.0's release run failed on exactly this).
/// </summary>
public sealed class PackedFeedVersionTests : IDisposable
{
    private readonly string _feed = Directory.CreateTempSubdirectory("rask-feed-version-").FullName;

    public void Dispose() => Directory.Delete(_feed, recursive: true);

    private void Pack(string file) => File.WriteAllBytes(Path.Combine(_feed, file), []);

    [Fact]
    public void A_stable_feed_finds_the_package_held_as_its_prerelease()
    {
        Pack("Rask.Server.0.24.0.nupkg");
        Pack("Rask.Postgres.0.24.0-rc.1.nupkg");

        var held = CliBuildE2E.PackedFile(_feed, "Rask.Postgres", "0.24.0");

        Assert.Equal(Path.Combine(_feed, "Rask.Postgres.0.24.0-rc.1.nupkg"), held);
    }

    [Fact]
    public void A_stable_feed_finds_every_other_package_at_that_version()
    {
        Pack("Rask.Server.0.24.0.nupkg");
        Pack("Rask.0.24.0.nupkg");

        var core = CliBuildE2E.PackedFile(_feed, "Rask", "0.24.0");

        Assert.Equal(Path.Combine(_feed, "Rask.0.24.0.nupkg"), core);
    }

    [Fact]
    public void A_prerelease_feed_takes_no_other_prerelease_for_a_missing_package()
    {
        Pack("Rask.Postgres.0.24.1-alpha.0.5-rc.1.nupkg");

        var missing = CliBuildE2E.PackedFile(_feed, "Rask.Postgres", "0.24.1-alpha.0.5");

        Assert.Null(missing);
    }

    [Fact]
    public void A_package_the_pack_never_wrote_is_reported_missing()
    {
        Pack("Rask.Server.0.24.0.nupkg");

        var missing = CliBuildE2E.PackedFile(_feed, "Rask.Postgres", "0.24.0");

        Assert.Null(missing);
    }
}
