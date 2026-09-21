namespace Rask.Redis;

/// <summary>
/// What goes over a Redis channel: the publishing host's id, then the message as the topic's JSON contract wrote it.
/// </summary>
/// <remarks>
/// Redis delivers a publish to every subscriber of the channel, the publishing connection's own included, and the
/// hub has already delivered that message to this host's subscribers. The id is how a host recognises its own
/// message and drops it, so nothing is delivered twice. Sixteen raw bytes rather than a JSON wrapper: the payload
/// is already JSON, and wrapping it would mean parsing every message twice.
/// </remarks>
internal static class BackplaneEnvelope
{
    private const int HostIdLength = 16;

    internal static byte[] Encode(Guid host, byte[] payload)
    {
        var envelope = new byte[HostIdLength + payload.Length];
        host.TryWriteBytes(envelope);
        payload.CopyTo(envelope.AsSpan(HostIdLength));
        return envelope;
    }

    /// <summary>
    /// The message in <paramref name="envelope" /> when another host published it; <see langword="false" /> for this
    /// host's own publish and for anything too short to be an envelope.
    /// </summary>
    internal static bool TryOpen(ReadOnlyMemory<byte> envelope, Guid host, out ReadOnlyMemory<byte> payload)
    {
        if (envelope.Length < HostIdLength || new Guid(envelope.Span[..HostIdLength]) == host)
        {
            payload = default;
            return false;
        }

        payload = envelope[HostIdLength..];
        return true;
    }
}
