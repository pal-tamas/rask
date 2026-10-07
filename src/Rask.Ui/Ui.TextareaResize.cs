namespace Rask;

public static partial class Ui
{
    /// <summary>Which way the reader may drag a <see cref="UiTextarea{T}" /> bigger.</summary>
    public enum TextareaResize
    {
        /// <summary>Taller or shorter.</summary>
        Vertical = 0,

        /// <summary>Wider or narrower.</summary>
        Horizontal,

        /// <summary>Either way.</summary>
        Both,

        /// <summary>Not at all.</summary>
        None,
    }
}
