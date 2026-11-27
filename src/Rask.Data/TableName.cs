using System.Text.Json;
using Rask.Generators.Shared;

namespace Rask.Data;

/// <summary>
///     A table being named by <c>TableNames</c>: the class it holds, and the names Rask can already give it —
///     <c>c.TableNames(t =&gt; t.Class)</c>.
/// </summary>
public sealed class TableName
{
    internal TableName(string @class, string asDeclared)
    {
        Class = @class;
        AsDeclared = asDeclared;
    }

    /// <summary>The entity's class name, as it is written: <c>Fueling</c>.</summary>
    public string Class { get; }

    /// <summary>What EF Core names it: the context's <c>DbSet</c> property for it (<c>Fuelings</c>), or else <see cref="Class" />.</summary>
    public string AsDeclared { get; }

    /// <summary>The class name in camel case: <c>fueling</c>, <c>deliveryNote</c>.</summary>
    public string CamelCase => JsonNamingPolicy.CamelCase.ConvertName(Class);

    /// <summary>The class name in snake case: <c>fueling</c>, <c>delivery_note</c>.</summary>
    public string SnakeCase => StoreNames.SnakeCase(Class);

    /// <summary>
    ///     The class name in snake case and plural, Rask's own name for the table: <c>fuelings</c>, <c>delivery_notes</c>,
    ///     <c>companies</c>, <c>people</c>.
    /// </summary>
    /// <remarks>
    ///     Rails' plural, of the last word: its rules (<c>-y</c> → <c>-ies</c>, <c>-s</c>/<c>-x</c>/<c>-ch</c>/<c>-sh</c> →
    ///     <c>-es</c>, <c>-f</c> → <c>-ves</c>, <c>-sis</c> → <c>-ses</c>), irregulars (<c>person</c>, <c>child</c>,
    ///     <c>man</c>) and uncountables (<c>equipment</c>, <c>information</c>, <c>series</c>), and a word that already ends in
    ///     <c>s</c> kept as it is (<c>stored_file_bytes</c>). Cut to 63 bytes, with a hash of the whole name at its end.
    /// </remarks>
    public string SnakeCasePlural => StoreNames.Table(Class);
}
