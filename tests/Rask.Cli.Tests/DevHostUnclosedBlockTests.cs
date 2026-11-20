using Rask.Hosting.Shared;

namespace Rask.Cli.Tests;

// A hosts block whose end marker is gone used to run to the end of the file, so the next `rask dev` — as root —
// rewrote, and `rask dev --undo` deleted, every entry the developer had below it.
public sealed class DevHostUnclosedBlockTests
{
    private const string Unclosed =
        "127.0.0.1\tlocalhost\n"
        + "\n"
        + DevHostFiles.HostsBeginMarker + "\n"
        + "# Added by `rask dev`. Delete this block to undo; it is rewritten, never duplicated.\n"
        + "127.0.0.1\tappname.test\n"
        + "10.0.0.5\tbuild-server\n"
        + "127.0.0.1 my-own.local\n";

    [Fact]
    public void Removing_an_unclosed_block_keeps_the_entries_after_it()
    {
        var removed = DevHostFiles.RemoveBlock(Unclosed);

        Assert.Equal("127.0.0.1\tlocalhost\n10.0.0.5\tbuild-server\n127.0.0.1 my-own.local\n", removed);
    }

    [Fact]
    public void Adding_to_an_unclosed_block_closes_it_and_keeps_the_entries_after_it()
    {
        var added = DevHostFiles.AddHost(Unclosed, "other.test");

        Assert.NotNull(added);
        Assert.Contains(DevHostFiles.HostsEndMarker + "\n10.0.0.5\tbuild-server\n127.0.0.1 my-own.local\n", added, StringComparison.Ordinal);
        Assert.Equal(["appname.test", "other.test"], DevHostFiles.ManagedHosts(added));
    }

    [Fact]
    public void A_record_that_is_not_ours_ends_the_block_even_when_it_points_at_loopback()
    {
        var hosts = DevHostFiles.HostsBeginMarker + "\n127.0.0.1\tappname.test\n127.0.0.1\tintranet.example\n";

        var managed = DevHostFiles.ManagedHosts(hosts);

        Assert.Equal(["appname.test"], managed);
    }
}
