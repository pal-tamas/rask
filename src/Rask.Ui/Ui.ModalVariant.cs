namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     How <see cref="UiModal" /> is drawn.
    /// </summary>
    public enum ModalVariant
    {
        /// <summary>A panel: rounded, shadowed, padded. The default.</summary>
        Default,

        /// <summary>A flyout that stands off the viewport's edges, rounded and shadowed like a panel.</summary>
        Floating,

        /// <summary>Nothing around the content — no panel and no close button — for content that draws its own.</summary>
        Bare,

        /// <summary>Flux's legacy spelling of <see cref="UiModal.Flyout" />.</summary>
        Flyout,
    }
}
