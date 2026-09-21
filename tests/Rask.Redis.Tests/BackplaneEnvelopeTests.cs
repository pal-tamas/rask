namespace Rask.Redis.Tests;

// #1115: Redis hands a publish back to the connection that sent it; the envelope's host id is how that echo is dropped.
public sealed class BackplaneEnvelopeTests
{
    private static readonly Guid Here = Guid.NewGuid();

    [Fact]
    public void Another_hosts_message_opens_to_its_payload()
    {
        var envelope = BackplaneEnvelope.Encode(Guid.NewGuid(), "{\"Id\":1}"u8.ToArray());

        Assert.True(BackplaneEnvelope.TryOpen(envelope, Here, out var payload));
        Assert.Equal("{\"Id\":1}"u8.ToArray(), payload.ToArray());
    }

    [Fact]
    public void This_hosts_own_message_is_dropped() =>
        Assert.False(BackplaneEnvelope.TryOpen(BackplaneEnvelope.Encode(Here, [1, 2, 3]), Here, out _));

    [Fact]
    public void Something_too_short_to_be_an_envelope_is_dropped() =>
        Assert.False(BackplaneEnvelope.TryOpen(new byte[15], Here, out _));

    [Fact]
    public void An_empty_payload_survives_the_trip()
    {
        Assert.True(BackplaneEnvelope.TryOpen(BackplaneEnvelope.Encode(Guid.NewGuid(), []), Here, out var payload));
        Assert.True(payload.IsEmpty);
    }
}
