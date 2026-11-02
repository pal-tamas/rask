using System.Diagnostics.CodeAnalysis;

namespace Rask.Cli.Commands;

/// <summary>
/// What the tunnel's ssh said about itself, from the file its <c>-E</c> sends its log to: why a forward failed, in
/// ssh's own words, rather than a deploy that only knows docker never answered.
/// </summary>
internal static class SshForwardLog
{
    // What ssh logs (at INFO) when the box turned a forwarded connection down: "channel 1: open failed: connect failed:
    // open failed" from an sshd whose AllowTcpForwarding is no — it gates unix sockets on that too — and the same
    // "open failed" from one that can't reach the socket.
    private const string ChannelRefused = "open failed";

    /// <summary>The log's last line, or <c>null</c> while it is empty or unreadable.</summary>
    internal static string? LastLine(string path)
    {
        try
        {
            // ssh still holds the file open for writing; share it rather than wait for the connection to end.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return LastNonEmpty(reader.ReadToEnd());
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Whether ssh logged that the box turned a forwarded connection down — no point waiting any longer.</summary>
    internal static bool IsRefusal([NotNullWhen(true)] string? line) => line?.Contains(ChannelRefused, StringComparison.Ordinal) == true;

    /// <summary>A refusal said in terms of what to look at on the box, with ssh's own line after it.</summary>
    internal static string Explain(string line) =>
        $"the box would not connect the forwarded docker socket — its sshd refuses forwarding (AllowTcpForwarding or "
        + $"AllowStreamLocalForwarding is 'no', or the key is no-port-forwarding), or docker is not at {DockerTunnel.RemoteSocket} (ssh: {line})";

    /// <summary>The last line of <paramref name="text"/> that says something, or <c>null</c>.</summary>
    internal static string? LastNonEmpty(string text) =>
        text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
}
