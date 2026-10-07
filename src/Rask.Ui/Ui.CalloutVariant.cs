namespace Rask;

public static partial class Ui
{
    /// <summary>What a <see cref="UiCallout" /> is telling its reader, and so which colours it is drawn in.</summary>
    public enum CalloutVariant
    {
        /// <summary>Worth knowing, nothing more: zinc. Flux's default.</summary>
        Secondary,

        /// <summary>It worked: green.</summary>
        Success,

        /// <summary>Not wrong yet: yellow.</summary>
        Warning,

        /// <summary>It failed, or is about to cost something: red.</summary>
        Danger,
    }
}
