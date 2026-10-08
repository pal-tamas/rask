namespace Rask;

public static partial class Ui
{
    /// <summary>Which characters a <see cref="UiOtp" /> takes.</summary>
    public enum OtpMode
    {
        /// <summary>Digits.</summary>
        Numeric = 0,

        /// <summary>Letters and digits; letters are upper-cased.</summary>
        Alphanumeric,

        /// <summary>Letters, upper-cased.</summary>
        Alpha,
    }
}
