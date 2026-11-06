namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     One row of another table as a select or a pillbox offers it: the value that is stored, the words that are
    ///     shown, and an optional muted line under them that a search finds the row by too.
    /// </summary>
    /// <remarks>
    ///     Handed to <c>.Choices(…)</c> on <c>Ui.Select</c> and <c>Ui.Pillbox</c>, which draw each as an option in the
    ///     reader's alphabet. A query can build one with the two-argument constructor, which an expression tree takes:
    ///     <c>Driver.Where(…).Select(d =&gt; new Ui.Choice&lt;int&gt;(d.Id, d.Name))</c>.
    /// </remarks>
    /// <typeparam name="T">The type of the value: the bound member's, or one of the bound collection's.</typeparam>
    /// <param name="Value">What picking the row stores.</param>
    /// <param name="Name">The words the row is shown, sorted and searched by.</param>
    /// <param name="Detail">
    ///     A muted line under the words in the list — a vehicle's serial under its plate — that a search finds the row by
    ///     too. The select does not repeat it once the row is picked, and a pill says the words alone.
    /// </param>
    public readonly record struct Choice<T>(T Value, string Name, string? Detail)
    {
        /// <summary>A row with no line under its words.</summary>
        /// <param name="value">What picking the row stores.</param>
        /// <param name="name">The words the row is shown, sorted and searched by.</param>
        public Choice(T value, string name)
            : this(value, name, null)
        {
        }
    }
}
