namespace Rask.Data;

/// <summary>
///     Marks the options extension of a provider that provides the text collation
///     <see cref="ModelConfigurationBuilderExtensions.OrderTextBy" /> names, under <see cref="Collation" /> — SQLite's
///     registered on every connection, PostgreSQL's created by its migrations — so the model may give its text columns
///     that collation.
/// </summary>
/// <remarks>Internal for the same reason as <see cref="IRangeExclusionEnforcer" />.</remarks>
internal interface ITextCollationEnforcer
{
    /// <summary>The name the collation is registered under on each connection, as the schema and the SQL write it.</summary>
    string Collation { get; }

    /// <summary>
    ///     The name of the collation <see cref="SearchTextExtensions.Loosely" /> compares by — case, accents, kana and width
    ///     aside — as the provider registers or creates it.
    /// </summary>
    string SearchCollation { get; }

    /// <summary>
    ///     The collation a text key column gets instead — a primary or alternate key, or a foreign key — or null to give it
    ///     <see cref="Collation" /> like every other text column.
    /// </summary>
    /// <remarks>
    ///     PostgreSQL's is the binary <c>"C"</c>: an id is compared byte for byte, its btree index deduplicates, and a
    ///     foreign key matches its principal by equality no ICU rule can make ambiguous.
    /// </remarks>
    string? KeyCollation => null;
}
