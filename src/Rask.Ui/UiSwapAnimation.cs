namespace Rask.Ui;

/// <summary>
/// How one face gives way to the other.
/// </summary>
public enum UiSwapAnimation
{
    /// <summary>Cross-fade. The default.</summary>
    Default = 0,

    /// <summary>Rotates as it changes.</summary>
    Rotate,

    /// <summary>Flips as it changes.</summary>
    Flip,
}
