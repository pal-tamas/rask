using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
/// Rewrites <c>Search(text)</c> and <c>FullText.Highlight</c>/<c>Snippet</c> into ordinary LINQ over the full-text
/// index entities, before EF Core translates the query.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>Search</c> becomes a join.</b> The index is filtered by <c>index = @match</c> (FTS5's equality form of
/// <c>MATCH</c>), joined to the searched rows by key, and ordered by <c>rank</c>; the rows are then selected back
/// out. Everything composed after it — <c>Where</c>, <c>Skip</c>, <c>Take</c>, <c>Count</c> — applies to the
/// matches, and a later <c>OrderBy</c> replaces the rank order, which is what a grid's column sort should do.
/// SQLite plans it as a scan of the full-text index followed by a primary-key seek per match.
/// </para>
/// <para>
/// <b><c>Highlight</c>/<c>Snippet</c> become correlated subqueries</b> over the index row of the projected entity,
/// matched against the same text. SQLite evaluates a projection's subquery only for the rows it returns, so under a
/// <c>Take</c> it runs once per visible result.
/// </para>
/// <para>
/// The match text is never touched here: <c>Search</c> compiled it when it was called, and EF Core has already
/// turned it into a parameter by the time this runs, so the rewritten query is cached once for every search.
/// </para>
/// </remarks>
internal sealed class FullTextSearchQueryInterceptor : IQueryExpressionInterceptor
{
    private static readonly MethodInfo PropertyMethod = typeof(EF).GetMethod(nameof(EF.Property))!;

    private static readonly MethodInfo HighlightMarker = typeof(FullText).GetMethod(nameof(FullText.Highlight))!;

    private static readonly MethodInfo SnippetMarker = typeof(FullText).GetMethod(nameof(FullText.Snippet))!;

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(queryExpression);
        ArgumentNullException.ThrowIfNull(eventData);

        var searches = new SearchFinder();
        searches.Visit(queryExpression);

        if (searches.Found.Count == 0 && !searches.UsesFunctions)
        {
            return queryExpression;
        }

        var model = eventData.Context?.Model
            ?? throw new InvalidOperationException("Full-text search needs a DbContext to read the index mapping from.");

        return new Rewriter(model, searches.Found).Visit(queryExpression);
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
                if (!Found.TryGetValue(type, out var matches))
                {
                    Found[type] = matches = [];
                }

                matches.Add(node.Arguments[1]);
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
                var source = Visit(node.Arguments[0]);
                return RewriteSearch(Index.For(model, node.Method.GetGenericArguments()[0]), source, node.Arguments[1]);
            }

            if (node.Method == HighlightMarker || node.Method == SnippetMarker)
            {
                return RewriteFunction(node);
            }

            return base.VisitMethodCall(node);
        }

        // ---- Search -------------------------------------------------------------------------------------------

        private static Expression RewriteSearch(Index index, Expression source, Expression match)
        {
            var entity = index.EntityType.ClrType;
            var hit = typeof(FullTextHit<>).MakeGenericType(entity);

            Expression hits;
            if (index.KeyEntity is null)
            {
                // Posts ⋈ (index WHERE index = @match) ON Posts.Id = index.rowid
                var key = index.Key[0];
                var matching = Where(index.IndexEntity, row => Equal(Property(row, FullTextSearchEntityConvention.Match, typeof(string)), match));

                var p = Expression.Parameter(entity, "p");
                var f = Expression.Parameter(typeof(Dictionary<string, object>), "f");
                var r = Expression.Parameter(entity, "r");
                var m = Expression.Parameter(typeof(Dictionary<string, object>), "m");

                hits = Expression.Call(
                    typeof(Queryable),
                    nameof(Queryable.Join),
                    [entity, typeof(Dictionary<string, object>), key.ClrType, hit],
                    source,
                    matching,
                    Expression.Quote(Expression.Lambda(Property(p, key.Name, key.ClrType), p)),
                    Expression.Quote(Expression.Lambda(Property(f, FullTextSearchEntityConvention.RowId, key.ClrType), f)),
                    Expression.Quote(Expression.Lambda(Hit(hit, r, Property(m, FullTextSearchEntityConvention.Rank, typeof(double))), r, m)));
            }
            else
            {
                // Posts ⋈ keys ON every key column ⋈ (index WHERE index = @match) ON keys.rowid = index.rowid
                var p = Expression.Parameter(entity, "p");
                var rank = RankOf(index, p, match);
                var r = Expression.Parameter(entity, "r");
                var s = Expression.Parameter(typeof(double), "s");

                hits = Expression.Call(
                    typeof(Queryable),
                    nameof(Queryable.SelectMany),
                    [entity, typeof(double), hit],
                    source,
                    Expression.Quote(Expression.Lambda(
                        typeof(Func<,>).MakeGenericType(entity, typeof(IEnumerable<double>)), rank, p)),
                    Expression.Quote(Expression.Lambda(Hit(hit, r, s), r, s)));
            }

            var h = Expression.Parameter(hit, "h");
            var ordered = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.OrderBy),
                [hit, typeof(double)],
                hits,
                Expression.Quote(Expression.Lambda(Expression.Property(h, nameof(FullTextHit<object>.Rank)), h)));

            var o = Expression.Parameter(hit, "o");
            return Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Select),
                [hit, entity],
                ordered,
                Expression.Quote(Expression.Lambda(Expression.Property(o, nameof(FullTextHit<object>.Item)), o)));
        }

        // keys.Where(k => k.K1 == p.K1 && …).Join(index.Where(f => f.Match == @match), k => k.rowid, f => f.rowid, (k, f) => f.Rank)
        private static Expression RankOf(Index index, Expression entity, Expression match)
        {
            var keys = MatchingKeys(index, entity);
            var matching = Where(index.IndexEntity, row => Equal(Property(row, FullTextSearchEntityConvention.Match, typeof(string)), match));

            var k = Expression.Parameter(typeof(Dictionary<string, object>), "k");
            var f = Expression.Parameter(typeof(Dictionary<string, object>), "f");
            var jk = Expression.Parameter(typeof(Dictionary<string, object>), "jk");
            var jf = Expression.Parameter(typeof(Dictionary<string, object>), "jf");

            return Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Join),
                [typeof(Dictionary<string, object>), typeof(Dictionary<string, object>), typeof(long), typeof(double)],
                keys,
                matching,
                Expression.Quote(Expression.Lambda(Property(k, FullTextSearchEntityConvention.RowId, typeof(long)), k)),
                Expression.Quote(Expression.Lambda(Property(f, FullTextSearchEntityConvention.RowId, typeof(long)), f)),
                Expression.Quote(Expression.Lambda(Property(jf, FullTextSearchEntityConvention.Rank, typeof(double)), jk, jf)));
        }

        private static Expression MatchingKeys(Index index, Expression entity) =>
            Where(index.KeyEntity!, row => index.Key
                .Select(key => Equal(Property(row, key.Name, key.ClrType), Property(entity, key.Name, key.ClrType)))
                .Aggregate(Expression.AndAlso));

        // ---- Highlight / Snippet ------------------------------------------------------------------------------

        private Expression RewriteFunction(MethodCallExpression node)
        {
            var name = node.Method.Name;
            var argument = Unwrap(node.Arguments[0]);

            if (argument is not MemberExpression { Expression: { } owner, Member: PropertyInfo member })
            {
                throw new InvalidOperationException(
                    $"FullText.{name} takes an indexed property of the searched entity directly, as in " +
                    $"Post.Search(text).Select(p => FullText.{name}(p.Title)), but was given '{node.Arguments[0]}'.");
            }

            var index = Index.For(model, owner.Type);
            var column = index.Spec.Properties.ToList().IndexOf(member.Name);
            if (column < 0)
            {
                throw new InvalidOperationException(
                    $"FullText.{name}({owner.Type.Name}.{member.Name}): {member.Name} is not one of the properties " +
                    $"{owner.Type.Name} indexes. Add it to HasFullTextSearch, or highlight one of " +
                    $"{string.Join(", ", index.Spec.Properties)}.");
            }

            if (!searches.TryGetValue(owner.Type, out var matches) || matches.Count == 0)
            {
                throw new InvalidOperationException(
                    $"FullText.{name} marks what a search matched, so it needs one: call it on a query that calls " +
                    $"Search(text), as in {owner.Type.Name}.Search(text).Select(p => FullText.{name}(p.{member.Name})).");
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"FullText.{name} cannot tell which of this query's {matches.Count} searches of {owner.Type.Name} " +
                    "to mark. Search each once, and project the highlights from that query.");
            }

            var match = matches[0];
            var visitedOwner = Visit(owner);

            var f = Expression.Parameter(typeof(Dictionary<string, object>), "f");
            Expression predicate = Equal(Property(f, FullTextSearchEntityConvention.Match, typeof(string)), match);

            predicate = index.KeyEntity is null
                ? Expression.AndAlso(
                    Equal(Property(f, FullTextSearchEntityConvention.RowId, index.Key[0].ClrType), Property(visitedOwner, index.Key[0].Name, index.Key[0].ClrType)),
                    predicate)
                : Expression.AndAlso(
                    Equal(Property(f, FullTextSearchEntityConvention.RowId, typeof(long)), FirstRowId(index, visitedOwner)),
                    predicate);

            var filtered = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Where),
                [typeof(Dictionary<string, object>)],
                new EntityQueryRootExpression(index.IndexEntity),
                Expression.Quote(Expression.Lambda(predicate, f)));

            var hidden = Property(f, FullTextSearchEntityConvention.Match, typeof(string));
            var start = Expression.Constant(FullText.MatchStart.ToString());
            var end = Expression.Constant(FullText.MatchEnd.ToString());

            Expression call = name == nameof(FullText.Highlight)
                ? Expression.Call(FullTextFunctions.HighlightMethod, hidden, Expression.Constant(column), start, end)
                : Expression.Call(
                    FullTextFunctions.SnippetMethod,
                    hidden,
                    Expression.Constant(column),
                    start,
                    end,
                    Expression.Constant("…"),
                    Words(Visit(node.Arguments[1])));

            var selected = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Select),
                [typeof(Dictionary<string, object>), typeof(string)],
                filtered,
                Expression.Quote(Expression.Lambda(call, f)));

            return Expression.Call(typeof(Queryable), nameof(Queryable.FirstOrDefault), [typeof(string)], selected);
        }

        // keys.Where(k => k.K1 == p.K1 && …).Select(k => k.rowid).FirstOrDefault()
        private static Expression FirstRowId(Index index, Expression entity)
        {
            var k = Expression.Parameter(typeof(Dictionary<string, object>), "k");
            var rowIds = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Select),
                [typeof(Dictionary<string, object>), typeof(long)],
                MatchingKeys(index, entity),
                Expression.Quote(Expression.Lambda(Property(k, FullTextSearchEntityConvention.RowId, typeof(long)), k)));

            return Expression.Call(typeof(Queryable), nameof(Queryable.FirstOrDefault), [typeof(long)], rowIds);
        }

        // FTS5's snippet() accepts 1 to 64 tokens and fails the statement outside that range.
        private static Expression Words(Expression words)
        {
            if (words is ConstantExpression { Value: int constant })
            {
                return Expression.Constant(Math.Clamp(constant, 1, 64));
            }

            var max = typeof(Math).GetMethod(nameof(Math.Max), [typeof(int), typeof(int)])!;
            var min = typeof(Math).GetMethod(nameof(Math.Min), [typeof(int), typeof(int)])!;
            return Expression.Call(min, Expression.Call(max, words, Expression.Constant(1)), Expression.Constant(64));
        }

        // ---- Helpers ------------------------------------------------------------------------------------------

        private static Expression Where(IEntityType entityType, Func<ParameterExpression, Expression> predicate)
        {
            var row = Expression.Parameter(typeof(Dictionary<string, object>), "row");
            return Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Where),
                [typeof(Dictionary<string, object>)],
                new EntityQueryRootExpression(entityType),
                Expression.Quote(Expression.Lambda(predicate(row), row)));
        }

        private static Expression Hit(Type hit, Expression item, Expression rank) =>
            Expression.MemberInit(
                Expression.New(hit),
                Expression.Bind(hit.GetProperty(nameof(FullTextHit<object>.Item))!, item),
                Expression.Bind(hit.GetProperty(nameof(FullTextHit<object>.Rank))!, rank));

        private static Expression Property(Expression instance, string name, Type type) =>
            Expression.Call(PropertyMethod.MakeGenericMethod(type), instance, Expression.Constant(name));

        private static Expression Equal(Expression left, Expression right) =>
            Expression.Equal(left, right.Type == left.Type ? right : Expression.Convert(right, left.Type));

        private static Expression Unwrap(Expression node) =>
            node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } cast
                ? Unwrap(cast.Operand)
                : node;
    }

    /// <summary>The searched entity's index, as mapped by <see cref="FullTextSearchEntityConvention"/>.</summary>
    private sealed record Index(
        IEntityType EntityType,
        FullTextSearchSpec Spec,
        IReadOnlyList<IProperty> Key,
        IEntityType IndexEntity,
        IEntityType? KeyEntity)
    {
        public static Index For(IModel model, Type type)
        {
            var entityType = model.FindEntityType(type);
            if (entityType is null
                || !FullTextSearchSpec.TryParse(entityType.FindAnnotation(FullTextSearchSpec.AnnotationName)?.Value, out var spec))
            {
                throw new InvalidOperationException(
                    $"{type.Name} has no full-text index to search. Declare one in its configuration, as in " +
                    $"builder.HasFullTextSearch(x => new {{ x.Title, x.Body }}), and add a migration.");
            }

            var indexEntity = model.FindEntityType(FullTextSearchEntityConvention.IndexEntityName(entityType))
                ?? throw new InvalidOperationException(
                    $"{type.Name} declares HasFullTextSearch, but its index is not mapped. Configure the context with " +
                    "UseRaskSqlite(services) from Rask.SQLite.EntityFrameworkCore.");

            return new Index(
                entityType,
                spec,
                entityType.FindPrimaryKey()!.Properties,
                indexEntity,
                model.FindEntityType(FullTextSearchEntityConvention.KeyEntityName(entityType)));
        }
    }
}

/// <summary>A searched row paired with its rank, between the join and the projection back to the row.</summary>
/// <typeparam name="TEntity">The searched entity.</typeparam>
internal sealed class FullTextHit<TEntity>
{
    public TEntity Item { get; set; } = default!;

    public double Rank { get; set; }
}
