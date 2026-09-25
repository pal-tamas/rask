using System.ComponentModel;

namespace Rask.Core.Browser;

/// <summary>The relay frame carrying one peer-to-peer payload.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SignalingSignal(string Type, string To, string Payload);
