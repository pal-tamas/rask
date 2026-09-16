namespace Rask.Ui;

/// <summary>
/// Where a dialog sits in the viewport.
/// </summary>
/// <remarks>
/// Separate from <see cref="UiPosition" /> because the vocabularies genuinely differ: a dialog is placed
/// against the viewport and has a <see cref="Middle" /> and reading-direction edges, while a dropdown or a
/// tooltip is placed on a side of its trigger and aligned along it with <see cref="UiAlign" />. Sharing one
/// enum would have offered every component members it has no class for.
/// </remarks>
public enum UiModalPosition
{
    /// <summary>The component's own default — a sheet on a phone, centred from <c>sm</c> up.</summary>
    Default = 0,

    /// <summary>Against the top edge.</summary>
    Top,

    /// <summary>Centred.</summary>
    Middle,

    /// <summary>Against the bottom edge, as a sheet.</summary>
    Bottom,

    /// <summary>Against the reading-start edge.</summary>
    Start,

    /// <summary>Against the reading-end edge.</summary>
    End,
}
