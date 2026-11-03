namespace Rask;

public static partial class Ui
{
    /// <summary>Whether a <see cref="UiOtp" /> sends its form by itself: Flux's <c>submit</c>.</summary>
    public enum OtpSubmit
    {
        /// <summary>Never; the form is sent the way any form is. Flux's default.</summary>
        Default = 0,

        /// <summary>The form the cells are in is sent once every cell is filled. Flux's <c>auto</c>.</summary>
        Auto,
    }
}
