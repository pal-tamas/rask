namespace Rask.Data;

/// <summary>
///     Marks the options extension of a provider whose every connection registers the text collation
///     <see cref="ModelConfigurationBuilderExtensions.OrderTextBy" /> names, under <see cref="Collation" /> — so the
///     model may give its text columns that collation, and a connection opened without it would fail.
/// </summary>
/// <remarks>Internal for the same reason as <see cref="IRangeExclusionEnforcer" />.</remarks>
internal interface ITextCollationEnforcer
{
    /// <summary>The name the collation is registered under on each connection, as the schema and the SQL write it.</summary>
    string Collation { get; }
}
