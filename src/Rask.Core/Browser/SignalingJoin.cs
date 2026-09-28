using System.ComponentModel;

namespace Rask.Core.Browser;

/// <summary>The join frame, as the relay expects it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SignalingJoin(string Type, string Room);
