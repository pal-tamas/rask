using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NpgsqlTypes;
using Rask.Data;

namespace Rask.Postgres;

/// <summary>
/// <c>HasFullTextSearch</c>, <c>Search(text)</c> and <c>FullText.Highlight</c>/<c>Snippet</c> on PostgreSQL (#1109).
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL has full-text search built in, and Npgsql maps it, so this is mostly wiring rather than SQL. Each
/// entity that declares an index gets a stored generated <c>tsvector</c> column over the declared properties
/// (<c>IsGeneratedTsVectorColumn</c>) and a GIN index on it — no triggers, because the database keeps a generated
/// column current itself. <c>Search(text)</c> becomes <c>vector @@ to_tsquery(config, 'w1' &amp; 'w2':*)</c> ranked by
/// <c>ts_rank_cd</c>, and the highlight markers become <c>ts_headline</c> with Rask's U+E000/U+E001 around each match,
/// so <c>UiHighlight</c> renders it unchanged.
/// </para>
/// <para>
/// The tokenizers map to text search configurations: <see cref="FullTextTokenizer.English" /> to PostgreSQL's
/// <c>english</c> (stemming, like FTS5's porter), <see cref="FullTextTokenizer.Unicode" /> to <c>rask_unicode</c> —
/// <c>simple</c> with <c>unaccent</c> in front of it, so <c>keres</c> finds <c>kérés</c> as it does on SQLite.
/// <c>unaccent()</c> itself is not IMMUTABLE and cannot sit in a generated column; a configuration that calls it as a
/// dictionary can, which is why it is a configuration, created by the migration that first needs it.
/// </para>
/// </remarks>
internal static class PostgresFullTextSearch
{
    /// <summary>The shadow property, and column, holding each searchable row's <c>tsvector</c>.</summary>
    public const string VectorProperty = "RaskSearchVector";

    public const string UnicodeConfiguration = "rask_unicode";

    public static string ConfigurationFor(FullTextSearchSpec spec) =>
        spec.Tokenizer == FullTextTokenizer.English ? "english" : UnicodeConfiguration;

    /// <summary>
    /// Creates <see cref="UnicodeConfiguration" /> if it is not there. Idempotent, so any migration that builds a
    /// Unicode index can run it; the extension is created in the database's default schema, the configuration in the
    /// migration's current one, which is where <c>to_tsvector('rask_unicode', …)</c> will look for it.
    /// </summary>
    public const string CreateUnicodeConfiguration = """
        CREATE EXTENSION IF NOT EXISTS unaccent;
        DO $rask$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_ts_config WHERE cfgname = 'rask_unicode' AND pg_ts_config_is_visible(oid)) THEN
                CREATE TEXT SEARCH CONFIGURATION rask_unicode (COPY = simple);
                ALTER TEXT SEARCH CONFIGURATION rask_unicode
                    ALTER MAPPING FOR hword, hword_part, word WITH unaccent, simple;
            END IF;
        END
        $rask$;
        """;
}

/// <summary>Registers the model and query halves of PostgreSQL full-text search on a context.</summary>
internal sealed class PostgresFullTextSearchOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IConventionSetPlugin, PostgresFullTextSearchConventionSetPlugin>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInterceptor, PostgresFullTextSearchQueryInterceptor>());
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using Rask PostgreSQL full-text search ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:PostgresFullTextSearch"] = "1";
    }
}

internal sealed class PostgresFullTextSearchConventionSetPlugin : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.Add(new PostgresFullTextSearchConvention());
        return conventionSet;
    }
}

/// <summary>The generated <c>tsvector</c> column and its GIN index, for every entity that declares a search index.</summary>
internal sealed class PostgresFullTextSearchConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            if (!FullTextSearchSpec.TryParse(entityType.FindAnnotation(FullTextSearchSpec.AnnotationName)?.Value, out var spec)
                || entityType.GetTableName() is null)
            {
                continue;
            }

            var vector = entityType.Builder.Property(typeof(NpgsqlTsVector), PostgresFullTextSearch.VectorProperty)
                ?? throw new InvalidOperationException(
                    $"'{entityType.DisplayName()}' declares HasFullTextSearch, but a property called "
                    + $"'{PostgresFullTextSearch.VectorProperty}' is already configured another way, so the search "
                    + "vector has nowhere to live.");

            // A real string[]: Npgsql stores the list as given and casts it back to string[] when it reads it, so the
            // read-only wrapper a collection expression would build fails the first migration with InvalidCastException.
            vector.IsGeneratedTsVectorColumn(PostgresFullTextSearch.ConfigurationFor(spec), spec.Properties.ToArray());

            entityType.Builder.HasIndex([vector.Metadata])?.HasMethod("GIN");
        }
    }
}

/// <summary>
/// Rewrites the <c>Search</c> marker into a match on the generated vector, and the highlight markers into
/// <c>ts_headline</c>, before EF Core translates the query.
/// </summary>
internal sealed class PostgresFullTextSearchQueryInterceptor : IQueryExpressionInterceptor
{
    private static readonly MethodInfo HighlightMarker = typeof(FullText).GetMethod(nameof(FullText.Highlight))!;
    private static readonly MethodInfo SnippetMarker = typeof(FullText).GetMethod(nameof(FullText.Snippet))!;

    private static readonly MethodInfo PropertyMethod =
        typeof(EF).GetMethod(nameof(EF.Property))!.MakeGenericMethod(typeof(NpgsqlTsVector));

    private static readonly MethodInfo ToTsQuery = typeof(NpgsqlFullTextSearchDbFunctionsExtensions).GetMethod(
        nameof(NpgsqlFullTextSearchDbFunctionsExtensions.ToTsQuery), [typeof(DbFunctions), typeof(string), typeof(string)])!;

    private static readonly MethodInfo Matches = typeof(NpgsqlFullTextSearchLinqExtensions).GetMethod(
        nameof(NpgsqlFullTextSearchLinqExtensions.Matches), [typeof(NpgsqlTsVector), typeof(NpgsqlTsQuery)])!;

    private static readonly MethodInfo RankCoverDensity = typeof(NpgsqlFullTextSearchLinqExtensions).GetMethod(
        nameof(NpgsqlFullTextSearchLinqExtensions.RankCoverDensity), [typeof(NpgsqlTsVector), typeof(NpgsqlTsQuery)])!;

    // (query, config, document, options) in some order: bound by parameter NAME, so a reordering between Npgsql
    // versions cannot silently swap the document and the configuration.
    private static readonly MethodInfo Headline = typeof(NpgsqlFullTextSearchLinqExtensions).GetMethod(
        nameof(NpgsqlFullTextSearchLinqExtensions.GetResultHeadline),
        [typeof(NpgsqlTsQuery), typeof(string), typeof(string), typeof(string)])!;

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(queryExpression);
        ArgumentNullException.ThrowIfNull(eventData);

        var finder = new SearchFinder();
        finder.Visit(queryExpression);
        if (finder.Found.Count == 0 && !finder.UsesFunctions)
        {
            return queryExpression;
        }

        var model = eventData.Context?.Model
            ?? throw new InvalidOperationException("Full-text search needs a DbContext to read the index mapping from.");
        return new Rewriter(model, finder.Found).Visit(queryExpression);
    }

    private static FullTextSearchSpec SpecFor(IModel model, Type clrType) =>
        model.FindEntityType(clrType) is { } entityType
        && FullTextSearchSpec.TryParse(entityType.FindAnnotation(FullTextSearchSpec.AnnotationName)?.Value, out var spec)
            ? spec
            : throw new InvalidOperationException(
                $"Search(text) on {clrType.Name} needs a full-text index: declare one with "
                + $"builder.HasFullTextSearch(x => new {{ x.Title, x.Body }}) in {clrType.Name}'s configuration.");

    // to_tsquery(config, @tsQuery)
    private static Expression Query(FullTextSearchSpec spec, Expression tsQuery) =>
        Expression.Call(
            ToTsQuery,
            // A constant, not the EF.Functions property: this tree is built after EF has already evaluated the
            // query's client-side parts, so a static property access left in it is one nothing will evaluate, and
            // the whole Where is reported untranslatable.
            Expression.Constant(EF.Functions),
            Expression.Constant(PostgresFullTextSearch.ConfigurationFor(spec)),
            tsQuery);

    // The entity itself, not Convert(entity, object): a reference type is already assignable to EF.Property's object
    // parameter, and EF recognises EF.Property only when its first argument is the entity it reads from.
    private static Expression Vector(Expression entity) =>
        Expression.Call(PropertyMethod, entity, Expression.Constant(PostgresFullTextSearch.VectorProperty));

    private static Expression Unquote(Expression expression) =>
        expression is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : expression;

    private sealed class SearchFinder : ExpressionVisitor
    {
        public Dictionary<Type, List<Expression>> Found { get; } = [];

        public bool UsesFunctions { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (FullTextMarkers.IsMatching(node.Method))
            {
                var type = node.Method.GetGenericArguments()[0];
                if (!Found.TryGetValue(type, out var queries))
                {
                    Found[type] = queries = [];
                }

                queries.Add(node.Arguments[2]);
            }
            else if (node.Method == HighlightMarker || node.Method == SnippetMarker)
            {
                UsesFunctions = true;
            }

            return base.VisitMethodCall(node);
        }
    }

    private sealed class Rewriter(IModel model, Dictionary<Type, List<Expression>> searches) : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (FullTextMarkers.IsMatching(node.Method))
            {
                return RewriteSearch(node, Visit(node.Arguments[0]), []);
            }

            // Search(text).ThenBy(k)…: a tie-breaker comes after the rank, not instead of it.
            if (TieBreakers(node) is { } chain)
            {
                return RewriteSearch(chain.Marker, Visit(chain.Marker.Arguments[0]), chain.Orderings);
            }

            if (node.Method == HighlightMarker || node.Method == SnippetMarker)
            {
                return RewriteFunction(node);
            }

            return base.VisitMethodCall(node);
        }

        private Expression RewriteSearch(
            MethodCallExpression marker,
            Expression source,
            List<(LambdaExpression Key, bool Descending)> tieBreakers)
        {
            var entity = marker.Method.GetGenericArguments()[0];
            var spec = SpecFor(model, entity);
            var tsQuery = marker.Arguments[2];

            // source.Where(e => e.vector @@ to_tsquery(config, @q))
            var e = Expression.Parameter(entity, "e");
            Expression result = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Where),
                [entity],
                source,
                Expression.Quote(Expression.Lambda(Expression.Call(Matches, Vector(e), Query(spec, tsQuery)), e)));

            // .OrderByDescending(e => ts_rank_cd(e.vector, to_tsquery(config, @q)))
            var r = Expression.Parameter(entity, "r");
            result = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.OrderByDescending),
                [entity, typeof(float)],
                result,
                Expression.Quote(Expression.Lambda(Expression.Call(RankCoverDensity, Vector(r), Query(spec, tsQuery)), r)));

            foreach (var (key, descending) in tieBreakers)
            {
                result = Expression.Call(
                    typeof(Queryable),
                    descending ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy),
                    [entity, key.ReturnType],
                    result,
                    Expression.Quote(key));
            }

            return result;
        }

        private static (MethodCallExpression Marker, List<(LambdaExpression Key, bool Descending)> Orderings)? TieBreakers(
            MethodCallExpression node)
        {
            var orderings = new List<(LambdaExpression Key, bool Descending)>();
            Expression current = node;

            while (current is MethodCallExpression { Method: { DeclaringType: var declaring, Name: var name } } call
                   && declaring == typeof(Queryable)
                   && name is nameof(Queryable.ThenBy) or nameof(Queryable.ThenByDescending)
                   && call.Arguments.Count == 2)
            {
                orderings.Insert(0, ((LambdaExpression)Unquote(call.Arguments[1]), name == nameof(Queryable.ThenByDescending)));
                current = call.Arguments[0];
            }

            return orderings.Count > 0 && current is MethodCallExpression marker && FullTextMarkers.IsMatching(marker.Method)
                ? (marker, orderings)
                : null;
        }

        // FullText.Highlight(p.Title)      → ts_headline(config, p.Title, to_tsquery(config, @q), 'StartSel=…, HighlightAll=true')
        // FullText.Snippet(p.Body, words)  → ts_headline(config, p.Body, to_tsquery(config, @q), 'StartSel=…, MaxWords=n, …')
        private Expression RewriteFunction(MethodCallExpression node)
        {
            var name = node.Method.Name;
            var argument = node.Arguments[0] is UnaryExpression { NodeType: ExpressionType.Convert } convert
                ? convert.Operand
                : node.Arguments[0];

            if (argument is not MemberExpression { Expression: { } owner, Member: PropertyInfo member })
            {
                throw new InvalidOperationException(
                    $"FullText.{name} takes an indexed property of the searched entity directly, as in " +
                    $"Post.Read.Search(text).Select(p => FullText.{name}(p.Title)), but was given '{node.Arguments[0]}'.");
            }

            var spec = SpecFor(model, owner.Type);
            if (!spec.Properties.Contains(member.Name))
            {
                throw new InvalidOperationException(
                    $"FullText.{name}({owner.Type.Name}.{member.Name}): {member.Name} is not one of the properties " +
                    $"{owner.Type.Name} indexes. Add it to HasFullTextSearch, or highlight one of " +
                    $"{string.Join(", ", spec.Properties)}.");
            }

            if (!searches.TryGetValue(owner.Type, out var queries) || queries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"FullText.{name} marks what a search matched, so it needs one: call it on a query that calls " +
                    $"Search(text), as in {owner.Type.Name}.Search(text).Select(p => FullText.{name}(p.{member.Name})).");
            }

            if (queries.Count > 1)
            {
                throw new InvalidOperationException(
                    $"FullText.{name} cannot tell which of this query's {queries.Count} searches of {owner.Type.Name} " +
                    "to mark. Search each once, and project the highlights from that query.");
            }

            var marks = $"StartSel={FullText.MatchStart}, StopSel={FullText.MatchEnd}, ";
            Expression options = name == nameof(FullText.Highlight)
                ? Expression.Constant(marks + "HighlightAll=true")
                : SnippetOptions(marks, Visit(node.Arguments[1]));

            var arguments = new Dictionary<string, Expression>(StringComparer.Ordinal)
            {
                ["query"] = Query(spec, queries[0]),
                ["config"] = Expression.Constant(PostgresFullTextSearch.ConfigurationFor(spec)),
                ["document"] = Visit(argument),
                ["options"] = options,
            };

            return Expression.Call(Headline, Headline.GetParameters().Select(p => arguments[p.Name!]));
        }

        // 'StartSel=…, MaxWords=' || n || ', MinWords=1, …', with n clamped the way SQLite's snippet clamps it (1–64)
        // and kept above MinWords, which ts_headline requires to be smaller. Built in SQL rather than here, so a word
        // count held in a variable works as it does on SQLite.
        private static Expression SnippetOptions(string marks, Expression words)
        {
            var clamp = Expression.Call(
                typeof(Math).GetMethod(nameof(Math.Max), [typeof(int), typeof(int)])!,
                Expression.Call(typeof(Math).GetMethod(nameof(Math.Min), [typeof(int), typeof(int)])!, words, Expression.Constant(64)),
                Expression.Constant(2));
            var concat = typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string), typeof(string)])!;
            return Expression.Call(
                concat,
                Expression.Constant(marks + "MaxWords="),
                Expression.Call(clamp, typeof(int).GetMethod(nameof(int.ToString), Type.EmptyTypes)!),
                Expression.Constant(", MinWords=1, MaxFragments=1, FragmentDelimiter=…"));
        }
    }
}
