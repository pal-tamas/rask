using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Rask.Data;

/// <summary>The columns an <c>UpdateWhere</c>'s setters write, for a lock that lets some be changed under it (<c>Except</c>).</summary>
internal static class BulkSetters
{
    /// <summary>
    ///     The properties <paramref name="set" /> sets — <c>s.SetProperty(p =&gt; p.Note, …)</c> is <c>Note</c> — or null when
    ///     they cannot be read, which a lock then takes as touching everything.
    /// </summary>
    internal static IReadOnlyCollection<string>? Touched<T>(Action<UpdateSettersBuilder<T>> set)
        where T : class
    {
        try
        {
#pragma warning disable EF1001 // the builder EF Core itself hands ExecuteUpdate: run here on a copy only to read which columns it sets
            var builder = new UpdateSettersBuilder<T>();
            set(builder);
            var built = builder.BuildSettersExpression();
#pragma warning restore EF1001
            var named = new Named();
            named.Visit(built);
            return named.Properties.Count > 0 ? named.Properties : null;
        }
#pragma warning disable CA1031 // an internal shape that changed means "unknown", which is the conservative answer
        catch (Exception)
        {
            return null;
        }
#pragma warning restore CA1031
    }

    // Every one-parameter lambda whose body is a property of its row — the selector half of a setter (a value that copies
    // another column is named too, which only makes the lock stricter).
    private sealed class Named : ExpressionVisitor
    {
        internal HashSet<string> Properties { get; } = new(StringComparer.Ordinal);

        protected override Expression VisitLambda<TDelegate>(Expression<TDelegate> node)
        {
            var body = node.Body is UnaryExpression { NodeType: ExpressionType.Convert } cast ? cast.Operand : node.Body;
            if (node.Parameters.Count == 1)
            {
                if (body is MemberExpression { Expression: ParameterExpression } member)
                {
                    Properties.Add(member.Member.Name);
                }
                else if (body is MethodCallExpression { Method.Name: nameof(EF.Property), Arguments: [_, ConstantExpression { Value: string name }] })
                {
                    Properties.Add(name);
                }
            }

            return base.VisitLambda(node);
        }
    }
}
