namespace Rask.Ui;

/// <summary>
/// How big a component is drawn.
/// </summary>
/// <remarks>
/// daisyUI's five steps, shared by nearly every component that has a size at all, so one enum serves them
/// rather than each component inventing its own three-value scale.
/// </remarks>
public enum UiSize
{
    /// <summary>The component's own default, whatever daisyUI's theme says that is.</summary>
    Default = 0,

    /// <summary>Smallest.</summary>
    Xs,

    /// <summary>Small.</summary>
    Sm,

    /// <summary>The middle step, stated explicitly.</summary>
    Md,

    /// <summary>Large.</summary>
    Lg,

    /// <summary>Largest.</summary>
    Xl,
}
