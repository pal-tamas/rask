namespace Rask.Ui;

/// <summary>
/// Which glow an <see cref="UiAura" /> draws.
/// </summary>
/// <remarks>
/// Decoration, all of it. Nothing here carries meaning the way <see cref="UiTone" /> does, so none of
/// these should be the only thing telling a reader something — a plan that is recommended says so in
/// words as well.
/// </remarks>
public enum UiAuraStyle
{
    /// <summary>daisyUI's plain aura. The default.</summary>
    Default = 0,

    /// <summary>A soft halo.</summary>
    Glow,

    /// <summary>Two colours meeting.</summary>
    Dual,

    /// <summary>An iridescent sweep.</summary>
    Holo,

    /// <summary>The full spectrum.</summary>
    Rainbow,

    /// <summary>Warm metal.</summary>
    Gold,

    /// <summary>Cool metal.</summary>
    Silver,
}
