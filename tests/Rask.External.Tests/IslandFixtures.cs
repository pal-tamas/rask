namespace Rask.External.Tests;

/// <summary>An island whose only interactive surface is a callback prop.</summary>
public sealed partial class Ticker : ReactComponent
{
    /// <summary>Runs when the ticker is clicked.</summary>
    public Callback? OnTick { get; set; }
}

/// <summary>An island with no callbacks at all.</summary>
public sealed partial class Readout : ReactComponent
{
    /// <summary>The value to show.</summary>
    public int Value { get; set; }
}
