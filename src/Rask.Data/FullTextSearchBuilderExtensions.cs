using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
/// Declares a full-text index on an entity. Each database spells full-text search differently — SQLite as an FTS5
/// virtual table — so Rask carries the index as model metadata and lets the provider emit whatever implements it.
/// </summary>
public static class FullTextSearchBuilderExtensions
{
    /// <summary>
    /// Makes <typeparamref name="TEntity"/> searchable by the text in <paramref name="properties"/>, so
    /// <c>Post.Search(text)</c> — or <c>db.Set&lt;Post&gt;().Search(text)</c> — returns the rows matching it, best
    /// match first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The index lives in the database and is kept current there, by triggers, so a row written by raw SQL, a
    /// bulk update or another process is searchable the moment it commits.
    /// </para>
    /// <para>
    /// It is created by migrations: adding, changing or removing this declaration produces a migration of its
    /// own, and the migration that creates the index also fills it from the rows already in the table. A database
    /// created with <c>EnsureCreated</c> does not get it.
    /// </para>
    /// <para>
    /// Supported by <c>UseRaskSqlite</c> today. On any other provider the app fails at startup rather than on
    /// the first search.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.HasFullTextSearch(p =&gt; new { p.Title, p.Body });
    /// </code>
    /// </example>
    /// <typeparam name="TEntity">The entity being made searchable.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="properties">
    /// The text properties to index — <c>p =&gt; p.Title</c>, or <c>p =&gt; new { p.Title, p.Body }</c>. Their
    /// order is the order <c>FullText.Highlight</c> and <c>FullText.Snippet</c> know them by.
    /// </param>
    /// <param name="tokenizer">How text is split into terms. Defaults to <see cref="FullTextTokenizer.Unicode"/>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="properties"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="properties"/> does not name plain properties, or names one twice.</exception>
    public static EntityTypeBuilder<TEntity> HasFullTextSearch<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, object?>> properties,
        FullTextTokenizer tokenizer = FullTextTokenizer.Unicode)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(properties);

        if (!Enum.IsDefined(tokenizer))
        {
            throw new ArgumentOutOfRangeException(nameof(tokenizer), tokenizer, "Not a FullTextTokenizer value.");
        }

        var names = PropertyExpressions.Many(properties, nameof(properties));
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
        {
            throw new ArgumentException($"'{nameof(properties)}' names the same property more than once.", nameof(properties));
        }

        builder.HasAnnotation(FullTextSearchSpec.AnnotationName, new FullTextSearchSpec(names, tokenizer).Serialize());
        return builder;
    }
}
