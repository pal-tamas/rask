namespace Rask.Redis;

/// <summary>
/// How <c>AddRaskRedisBackplane()</c> uses Redis, bound from the <c>Rask:Redis</c> configuration section. The
/// connection string itself is <c>Rask:ConnectionStrings:Redis</c>.
/// </summary>
public sealed class RedisOptions
{
    /// <summary>
    /// What every broadcast channel's name starts with; the topic's name follows it. Defaults to
    /// <c>rask:broadcast:</c>. Give each app its own prefix when two apps share one Redis server, or a publish in one
    /// reaches the other's subscribers of a topic with the same name.
    /// </summary>
    public string ChannelPrefix { get; set; } = "rask:broadcast:";

    /// <summary>Throws when the options cannot name a channel.</summary>
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ChannelPrefix))
        {
            throw new InvalidOperationException(
                $"{nameof(RedisOptions)}.{nameof(ChannelPrefix)} is empty. Without a prefix every Redis channel "
                + "named like a topic would be read as one.");
        }
    }
}
