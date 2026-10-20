namespace Rask.Cli.Commands;

/// <summary>
/// How a deploy waits for a lock: <see cref="PollDelay"/> between attempts (zero in tests), <see cref="Attempts"/>
/// before giving up, and how often a held app lock's lease is renewed (<see cref="Heartbeat"/>).
/// </summary>
internal sealed record LockTiming(TimeSpan PollDelay, int Attempts, TimeSpan Heartbeat);
