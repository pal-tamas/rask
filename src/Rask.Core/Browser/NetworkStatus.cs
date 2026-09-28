namespace Rask.Core.Browser;

/// <summary>A snapshot of the network connection (the Network Information API).</summary>
/// <param name="EffectiveType">Effective connection class, derived from recent throughput/RTT.</param>
/// <param name="Downlink">Estimated downlink bandwidth in megabits per second.</param>
/// <param name="Rtt">Estimated effective round-trip time in milliseconds.</param>
/// <param name="SaveData">Whether the user has requested reduced data usage (Data Saver).</param>
public sealed record NetworkStatus(EffectiveConnectionType EffectiveType, double Downlink, double Rtt, bool SaveData);
