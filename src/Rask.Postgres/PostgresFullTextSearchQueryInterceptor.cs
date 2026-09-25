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

        private static FullTextSearchSpec SpecFor(IModel model, Type clrType) =>
            model.FindEntityType(clrType) is { } entityType
            && FullTextSearchSpec.TryParse(entityType.FindAnnotation(FullTextSearchSpec.AnnotationName)?.Value, out var spec)
                ? spec
                : throw new InvalidOperationException(
                    $"Search(text) on {clrType.Name} needs a full-text index: declare one with "
                    + $"builder.HasFullTextSearch(x => new {{ x.Title, x.Body }}) in {clrType.Name}'s configuration.");

        // to_tsquery(config, @tsQuery)
        private static MethodCallExpression Query(FullTextSearchSpec spec, Expression tsQuery) =>
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
        private static MethodCallExpression Vector(Expression entity) =>
            Expression.Call(PropertyMethod, entity, Expression.Constant(PostgresFullTextSearch.VectorProperty));

        private static Expression Unquote(Expression expression) =>
            expression is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : expression;

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
                orderings.Insert(0, ((LambdaExpression)Unquote(call.Arguments[1]), string.Equals(name, nameof(Queryable.ThenByDescending), StringComparison.Ordinal)));
                current = call.Arguments[0];
            }

            return orderings.Count > 0 && current is MethodCallExpression marker && FullTextMarkers.IsMatching(marker.Method)
                ? (marker, orderings)
                : null;
        }

        // FullText.Highlight(p.Title)      → ts_headline(config, p.Title, to_tsquery(config, @q), 'StartSel=…, HighlightAll=true')
        // FullText.Snippet(p.Body, words)  → ts_headline(config, p.Body, to_tsquery(config, @q), 'StartSel=…, MaxWords=n, …')
        private MethodCallExpression RewriteFunction(MethodCallExpression node)
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
            if (!spec.Properties.Contains(member.Name, StringComparer.Ordinal))
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
            Expression options = string.Equals(name, nameof(FullText.Highlight), StringComparison.Ordinal)
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
        private static MethodCallExpression SnippetOptions(string marks, Expression words)
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
