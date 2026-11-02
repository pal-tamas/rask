using System.Diagnostics.CodeAnalysis;

namespace Rask.Cli.Commands;

/// <summary>
/// What the tunnel's ssh said about itself, from the file its <c>-E</c> sends its log to: why a forward failed, in
/// ssh's own words, rather than a deploy that only knows docker never answered.
/// </summary>
internal static class SshForwardLog
{
    // What ssh logs when the box turned a forwarded connection down — sshd refused it, or the socket couldn't be reached.
    private const string ChannelRefused = "open failed";

    // The refusal sshd gives when its config, or the key's options, forbid forwarding.
    private const string Prohibited = "administratively prohibited";

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

    /// <summary>
    /// The refusal said in terms of what to change on the box, when it is sshd's config that forbids it; any other
    /// line as ssh wrote it.
    /// </summary>
    internal static string Explain(string line) =>
        line.Contains(Prohibited, StringComparison.Ordinal)
            ? $"the box's sshd refuses forwarding — AllowTcpForwarding or AllowStreamLocalForwarding is 'no', or the key is no-port-forwarding (ssh: {line})"
            : line;

    /// <summary>The last line of <paramref name="text"/> that says something, or <c>null</c>.</summary>
    internal static string? LastNonEmpty(string text) =>
        text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
}
