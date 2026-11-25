using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace Rask.Data;

/// <summary>
///     A picker's rows read from a query: what <see cref="ModelQuery{TEntity}.Choices{TKey}" /> hands out, and what the
///     generated <c>X.Choice(id, …)</c>, <c>X.Choices(…)</c> and <c>X.ChoicePage(…)</c> read through.
/// </summary>
/// <remarks>
///     <para>
///         A page is the rows whose words hold what was typed, case and accents aside (<c>Loosely()</c>), in the app's text
///         order (<c>InTextOrder()</c>, by the column as it is where the app names none), then by id so no row is on two
///         pages — and how many the search finds, and the picked rows, all read through one context: the tenant's and the
///         soft delete's filters decide what is there, so another tenant's id is answered with nothing.
///     </para>
///     <para>
///         The count is read only when the page does not say it: a first page shorter than it was asked to be is all
///         there is.
///     </para>
/// </remarks>
/// <typeparam name="TRow">The read face.</typeparam>
/// <typeparam name="TKey">The id's type.</typeparam>
internal sealed class ModelChoices<[DynamicallyAccessedMembers(DataTrimming.Entity)] TRow, TKey> : IChoiceSource<TKey>
    where TRow : class
    where TKey : notnull
{
    private static readonly MethodInfo LooselyMethod =
        ((MethodCallExpression)((Expression<Func<string, string>>)(s => s.Loosely())).Body).Method;

    private static readonly MethodInfo InTextOrderMethod =
        ((MethodCallExpression)((Expression<Func<string, string>>)(s => s.InTextOrder())).Body).Method;

    // string.ToLower(), which EF Core writes as SQL's lower(): never run here, so no culture of this process applies.
    private static readonly MethodInfo ToLowerMethod = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;

    private static readonly MethodInfo ContainsText =
        ((MethodCallExpression)((Expression<Func<string, bool>>)(s => s.Contains(""))).Body).Method;

    private static readonly MethodInfo ContainsKey =
        ((MethodCallExpression)((Expression<Func<List<TKey>, TKey, bool>>)((list, key) => list.Contains(key))).Body).Method;

    private static readonly FieldInfo BoxedKeys = typeof(StrongBox<List<TKey>>).GetField(nameof(StrongBox<List<TKey>>.Value))!;

    private static readonly FieldInfo BoxedText = typeof(StrongBox<string>).GetField(nameof(StrongBox<string>.Value))!;

    private readonly ModelQuery<TRow> _rows;
    private readonly Expression<Func<TRow, TKey>> _id;
    private readonly Expression<Func<TRow, string>> _text;
    private readonly Expression<Func<TRow, string?>>? _detail;
    private readonly Expression<Func<TRow, ChoiceRow<TKey>>> _row;

    internal ModelChoices(
        ModelQuery<TRow> rows, Expression<Func<TRow, TKey>> id, Expression<Func<TRow, string>> text, Expression<Func<TRow, string?>>? detail)
    {
        _rows = rows;
        _id = id;
        _text = text;
        _detail = detail;
        _row = RowOf(id, text, detail);
    }

    /// <inheritdoc />
    public Task<ChoiceRowPage<TKey>> Page(
        string typed, int skip, int take, IReadOnlyCollection<TKey>? picked = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegative(take);

        var words = typed.Trim();
        return _rows.Read(
            async (context, rows, token) =>
            {
                var found = words.Length == 0 ? rows : rows.Where(Holding(words, Loosens(context)));
                var page = await Ordered(found, context.Model).Skip(skip).Take(take).Select(_row).ToListAsync(token).ConfigureAwait(false);
                var total = skip == 0 && page.Count < take ? page.Count : await found.CountAsync(token).ConfigureAwait(false);
                var chosen = picked is { Count: > 0 } ? await Picked(rows, picked, token).ConfigureAwait(false) : [];
                return new ChoiceRowPage<TKey>(page, total, chosen);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ChoiceRow<TKey>?> Find(TKey id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _rows.Read(
            async (_, rows, token) =>
            {
                var found = await rows.Where(Holds([id])).Select(_row).Take(1).ToListAsync(token).ConfigureAwait(false);
                return found.Count == 0 ? (ChoiceRow<TKey>?)null : found[0];
            },
            cancellationToken);
    }

    // The picked rows there are, in the order they were asked for.
    private async Task<List<ChoiceRow<TKey>>> Picked(IQueryable<TRow> rows, IReadOnlyCollection<TKey> picked, CancellationToken cancellationToken)
    {
        var ids = picked.Distinct().ToList();
        var found = await rows.Where(Holds(ids)).Select(_row).ToListAsync(cancellationToken).ConfigureAwait(false);
        var byId = found.ToDictionary(static row => row.Id);
        return [.. ids.Where(byId.ContainsKey).Select(id => byId[id])];
    }

    // row => ids.Contains(row.Id), the list a parameter of the SQL.
    private Expression<Func<TRow, bool>> Holds(List<TKey> ids) =>
        Expression.Lambda<Func<TRow, bool>>(
            Expression.Call(Expression.Field(Expression.Constant(new StrongBox<List<TKey>>(ids)), BoxedKeys), ContainsKey, _id.Body),
            _id.Parameters);

    // Whether the context compares text case and accents aside (Loosely()): what SearchTextConvention translates.
    private static bool Loosens(DbContext context) =>
        string.Equals(context.Database.ProviderName, ProviderNames.SqlServer, StringComparison.Ordinal) ||
        context.GetService<IDbContextOptions>().Extensions.OfType<ITextCollationEnforcer>().Any();

    // row => row.Text.Loosely().Contains(words) || row.Detail.Loosely().Contains(words): case and accents aside where the
    // provider knows how, as a search of a list in memory looks ("kovacs" finds "Kovács"); else case aside, by ToLower.
    private Expression<Func<TRow, bool>> Holding(string words, bool loosely)
    {
        var row = _text.Parameters[0];
        var typed = Expression.Field(
            Expression.Constant(new StrongBox<string>(loosely ? words : words.ToLowerInvariant())), BoxedText);

        Expression Holds(Expression text) =>
            Expression.Call(loosely ? Expression.Call(LooselyMethod, text) : Expression.Call(text, ToLowerMethod), ContainsText, typed);

        Expression held = Holds(_text.Body);
        if (_detail is not null)
        {
            var detail = ReplacingExpressionVisitor.Replace(_detail.Parameters[0], row, _detail.Body);
            held = Expression.OrElse(held, Expression.AndAlso(Expression.NotEqual(detail, Expression.Constant(null, typeof(string))), Holds(detail)));
        }

        return Expression.Lambda<Func<TRow, bool>>(held, row);
    }

    // By the words in the app's text order where its model names one, then by the id.
    private IOrderedQueryable<TRow> Ordered(IQueryable<TRow> rows, IModel model)
    {
        var byText = model.FindDbFunction(InTextOrderMethod) is null
            ? _text
            : Expression.Lambda<Func<TRow, string>>(Expression.Call(InTextOrderMethod, _text.Body), _text.Parameters);
        return rows.OrderBy(byText).ThenBy(_id);
    }

    // row => new ChoiceRow<TKey>(row.Id, row.Text, row.Detail), over the one row parameter.
    private static Expression<Func<TRow, ChoiceRow<TKey>>> RowOf(
        Expression<Func<TRow, TKey>> id, Expression<Func<TRow, string>> text, Expression<Func<TRow, string?>>? detail)
    {
        var row = id.Parameters[0];
        var constructor = typeof(ChoiceRow<TKey>).GetConstructor([typeof(TKey), typeof(string), typeof(string)])!;
        return Expression.Lambda<Func<TRow, ChoiceRow<TKey>>>(
            Expression.New(
                constructor,
                id.Body,
                ReplacingExpressionVisitor.Replace(text.Parameters[0], row, text.Body),
                detail is null
                    ? Expression.Constant(null, typeof(string))
                    : ReplacingExpressionVisitor.Replace(detail.Parameters[0], row, detail.Body)),
            row);
    }
}
