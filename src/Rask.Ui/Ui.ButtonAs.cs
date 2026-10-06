namespace Rask;

public static partial class Ui
{
    /// <summary>Which element a <see cref="UiButton" /> is. Flux's <c>as</c>.</summary>
    public enum ButtonAs
    {
        /// <summary>A <c>&lt;button&gt;</c>. The default.</summary>
        Button,

        /// <summary>An <c>&lt;a&gt;</c>, which <see cref="UiButton.Href" /> picks on its own.</summary>
        A,

        /// <summary>A <c>&lt;div&gt;</c>: the look of a button on something that is not one.</summary>
        Div,
    }
}
