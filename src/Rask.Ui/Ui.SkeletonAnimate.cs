namespace Rask;

public static partial class Ui
{
    /// <summary>How a skeleton shows that something is on its way.</summary>
    public enum SkeletonAnimate
    {
        /// <summary>Still. The default.</summary>
        None = 0,

        /// <summary>A band of light crossing it, left to right, every two seconds.</summary>
        Shimmer,

        /// <summary>Fading to half and back, every two seconds.</summary>
        Pulse,
    }
}
