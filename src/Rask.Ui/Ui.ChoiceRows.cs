using System.Collections;

namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     A page of rows a paging <c>.Choices((typed, page) =&gt; …)</c> answers with, and how many rows there are in all
    ///     for what was typed: the list's scrollbar is then as long as the whole list from its first page on, and the reader
    ///     drags it anywhere, as in a native list.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Returned where the page of rows is: it is that page (<see cref="IReadOnlyList{T}" />), so the finder's type
    ///         stays <c>Task&lt;IReadOnlyList&lt;Ui.Choice&lt;T&gt;&gt;&gt;</c>. A page that is a plain list says nothing of
    ///         the rest, and the list grows a page at a time as the reader scrolls on.
    ///     </para>
    ///     <para>
    ///         The rows not found yet are drawn as skeleton rows, and found — however far the reader scrolled — with one
    ///         question for every row up to where the box shows. <paramref name="total" /> is asked again with every page:
    ///         a count that changed since is taken as it is now.
    ///     </para>
    ///     <code>
    ///     var found = query.Where(…).OrderBy(…);
    ///     return new Ui.ChoiceRows&lt;int&gt;(await found.Skip(page.Skip).Take(page.Take).Select(…).ToListAsync(), await found.CountAsync());
    ///     </code>
    /// </remarks>
    /// <param name="rows">The page of rows: at most <see cref="ChoicePage.Take" />, after the first <see cref="ChoicePage.Skip" />.</param>
    /// <param name="total">How many rows answer what was typed, the ones on every page together.</param>
    /// <typeparam name="T">The type of a row's value.</typeparam>
    public sealed class ChoiceRows<T>(IReadOnlyList<Choice<T>> rows, int total) : IReadOnlyList<Choice<T>>
    {
        private readonly IReadOnlyList<Choice<T>> _rows = rows ?? throw new ArgumentNullException(nameof(rows));

        /// <summary>How many rows answer what was typed, the ones on every page together.</summary>
        public int Total { get; } = Math.Max(0, total);

        /// <inheritdoc />
        public int Count => _rows.Count;

        /// <inheritdoc />
        public Choice<T> this[int index] => _rows[index];

        /// <inheritdoc />
        public IEnumerator<Choice<T>> GetEnumerator() => _rows.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
