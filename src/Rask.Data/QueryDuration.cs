using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.Data;

/// <summary>
///     <c>end - start</c> in a query — two <see cref="DateTime" />s or two <see cref="DateTimeOffset" />s making a
///     <see cref="TimeSpan" /> — as a call a provider's translator can write in SQL.
/// </summary>
/// <remarks>
///     <para>
///         SQL Server and SQLite have no type a length of time fits in, so EF Core translates neither the subtraction
///         nor anything done with its result. <see cref="Interceptor" /> rewrites each subtraction into
///         <see cref="Between(DateTime, DateTime)" />, and the provider writes that as a whole number of microseconds,
///         typed <see cref="TimeSpan" /> under <see cref="MicrosecondsConverter" />: a <see cref="TimeSpan" /> on the
///         other side of a comparison is then converted to the same count, an <c>OrderBy</c> orders by it, and a
///         projection reads a <see cref="TimeSpan" /> back.
///     </para>
///     <para>
///         PostgreSQL translates all of it itself, so its provider never registers the rewrite.
///     </para>
///     <para>
///         Source-linked into <c>Rask.SqlServer</c>, which does not reference this assembly; internal in both.
///     </para>
/// </remarks>
internal static class QueryDuration
{
    private static readonly MethodInfo[] Markers =
    [
        new Func<DateTime, DateTime, TimeSpan>(Between).Method,
        new Func<DateTime?, DateTime?, TimeSpan?>(Between).Method,
        new Func<DateTimeOffset, DateTimeOffset, TimeSpan>(Between).Method,
        new Func<DateTimeOffset?, DateTimeOffset?, TimeSpan?>(Between).Method,
    ];

    /// <summary>The query interceptor that rewrites every <c>end - start</c> into <see cref="Between(DateTime, DateTime)" />.</summary>
    internal static readonly IQueryExpressionInterceptor Interceptor = new Rewrite();

    /// <summary>How long from <paramref name="start" /> to <paramref name="end" />: <c>end - start</c>.</summary>
    /// <remarks>What runs when EF Core evaluates the call in memory, in a projection it does not translate.</remarks>
    internal static TimeSpan Between(DateTime end, DateTime start) => end - start;

    /// <inheritdoc cref="Between(DateTime, DateTime)" />
    internal static TimeSpan? Between(DateTime? end, DateTime? start) => end - start;

    /// <inheritdoc cref="Between(DateTime, DateTime)" />
    internal static TimeSpan Between(DateTimeOffset end, DateTimeOffset start) => end - start;

    /// <inheritdoc cref="Between(DateTime, DateTime)" />
    internal static TimeSpan? Between(DateTimeOffset? end, DateTimeOffset? start) => end - start;

    /// <summary>Whether <paramref name="method" /> is one of the <see cref="Between(DateTime, DateTime)" /> overloads.</summary>
    internal static bool IsBetween(MethodInfo method) => Array.IndexOf(Markers, method) >= 0;

    /// <summary>The factor that turns the stored microseconds into <paramref name="member" />, or null for any other member.</summary>
    /// <remarks><c>TotalSeconds</c> is the count divided by a million, <c>TotalDays</c> by 86&#160;400 million.</remarks>
    internal static double? MicrosecondsPer(MemberInfo member) =>
        member.DeclaringType != typeof(TimeSpan)
            ? null
            : member.Name switch
            {
                nameof(TimeSpan.TotalMilliseconds) => 1e3,
                nameof(TimeSpan.TotalSeconds) => 1e6,
                nameof(TimeSpan.TotalMinutes) => 60e6,
                nameof(TimeSpan.TotalHours) => 3_600e6,
                nameof(TimeSpan.TotalDays) => 86_400e6,
                _ => null,
            };

    /// <summary>A <see cref="TimeSpan" /> as the whole microseconds the SQL computes — finer than that is dropped.</summary>
    internal sealed class MicrosecondsConverter() : ValueConverter<TimeSpan, long>(
        static span => span.Ticks / TimeSpan.TicksPerMicrosecond,
        static microseconds => TimeSpan.FromTicks(microseconds * TimeSpan.TicksPerMicrosecond));

    private sealed class Rewrite : IQueryExpressionInterceptor
    {
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData) =>
            Subtractions.Instance.Visit(queryExpression);
    }

    private sealed class Subtractions : ExpressionVisitor
    {
        internal static readonly Subtractions Instance = new();

        protected override Expression VisitBinary(BinaryExpression node)
        {
            var visited = base.VisitBinary(node);
            return visited is BinaryExpression { NodeType: ExpressionType.Subtract or ExpressionType.SubtractChecked } subtraction
                   && Marker(subtraction) is { } marker
                ? Expression.Call(marker, Lift(subtraction.Left, marker), Lift(subtraction.Right, marker))
                : visited;
        }

        // The overload whose operands the subtraction's are, lifted when either is nullable — as C# lifts the operator.
        private static MethodInfo? Marker(BinaryExpression subtraction)
        {
            var left = Nullable.GetUnderlyingType(subtraction.Left.Type) ?? subtraction.Left.Type;
            var right = Nullable.GetUnderlyingType(subtraction.Right.Type) ?? subtraction.Right.Type;
            if (left != right || (left != typeof(DateTime) && left != typeof(DateTimeOffset)))
            {
                return null;
            }

            var lifted = subtraction.Left.Type != left || subtraction.Right.Type != right;
            return (left == typeof(DateTime), lifted) switch
            {
                (true, false) => Markers[0],
                (true, true) => Markers[1],
                (false, false) => Markers[2],
                (false, true) => Markers[3],
            };
        }

        private static Expression Lift(Expression operand, MethodInfo marker)
        {
            var wanted = marker.GetParameters()[0].ParameterType;
            return operand.Type == wanted ? operand : Expression.Convert(operand, wanted);
        }
    }
}
