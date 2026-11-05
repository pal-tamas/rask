namespace Rask.Data;

/// <summary>Text in the order the app's model names, inside a query.</summary>
public static class TextOrderExtensions
{
    /// <summary>
    ///     The text, ordered as the database's collation the model names with <c>c.OrderTextBy(…)</c>:
    ///     <c>Driver.OrderBy(d =&gt; d.Name.InTextOrder())</c>.
    /// </summary>
    /// <param name="text">A text column.</param>
    /// <returns>Never returns: it is only written into a query's SQL.</returns>
    /// <remarks>
    ///     <para>
    ///         On SQL Server it is written <c>[Name] COLLATE Hungarian_100_CI_AS</c> — in that <c>ORDER BY</c> only, so the
    ///         column, its schema and every <c>Where</c> on it stay as they are. On every other database it orders by the
    ///         column as it is.
    ///     </para>
    ///     <para>
    ///         It runs in the database: a list already in memory orders with <c>TextOrder.In("hu")</c> or
    ///         <c>TextOrder.Current</c> instead.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Called outside a query the database runs.</exception>
    public static string InTextOrder(this string text) =>
        throw new InvalidOperationException(
            "x.InTextOrder() runs in the database, in a query's OrderBy, once the app's IRaskModel names the order with "
            + "c.OrderTextBy(\"Hungarian_100_CI_AS\") in its Conventions. A list already in memory orders with "
            + "list.OrderBy(x => x.Name, TextOrder.In(\"hu\")).");
}
