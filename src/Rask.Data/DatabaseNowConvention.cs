using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     Writes the database's clock as a column's default — what <c>Default(x =&gt; x.CreatedAt, () =&gt; Clock.Now)</c>
///     declared — as the provider's own SQL, and forgets the declaration.
/// </summary>
/// <param name="sql">The provider's dialect, or null for a provider that writes no SQL.</param>
internal sealed class DatabaseNowConvention(ModelSql? sql) : IModelFinalizingConvention
{
    /// <summary>The provider-neutral declaration: the column defaults to now.</summary>
    internal const string Annotation = "Rask:DefaultNow";

    /// <summary>Refuses anything but the clock's now, where the default is declared.</summary>
    /// <param name="now">The lambda's body.</param>
    /// <param name="declaration">The declaration, for the refusal to name.</param>
    /// <exception cref="ArgumentException">It is something else.</exception>
    internal static void Check(Expression now, string declaration)
    {
        if (now is MemberExpression { Member.Name: nameof(Clock.Wall) or nameof(Clock.Today) } wall
            && wall.Member.DeclaringType == typeof(Clock))
        {
            throw new ArgumentException(
                $"{declaration}: the database does not know the app's time zone, so it has no wall clock. Default to "
                + "() => Clock.Now, a moment in UTC.",
                nameof(now));
        }

        if (!IsNow(now))
        {
            throw new ArgumentException(
                $"{declaration}: a lambda default is the database's clock, () => Clock.Now (or () => Clock.Now.UtcDateTime "
                + "for a DateTime). A constant goes as itself: Default(x => x.Size, 1000).",
                nameof(now));
        }
    }

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var property in entityType.GetDeclaredProperties())
            {
                if (property.FindAnnotation(Annotation) is null)
                {
                    continue;
                }

                property.RemoveAnnotation(Annotation);
                if (sql is null)
                {
                    continue;
                }

                sql.Supported($"Default(x => x.{property.Name}, () => Clock.Now)");
                var storeType = property.GetColumnType() ?? ((RelationalTypeMapping?)property.FindTypeMapping())?.StoreType;
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                property.SetDefaultValueSql(sql.Now(type, storeType), fromDataAnnotation: true);
            }
        }
    }

    // Clock.Now, Clock.Now.UtcDateTime, DateTimeOffset.UtcNow, DateTime.UtcNow — and nothing that reads another moment.
    private static bool IsNow(Expression now) => now switch
    {
        UnaryExpression { NodeType: ExpressionType.Convert } lifted => IsNow(lifted.Operand),
        MemberExpression { Expression: null, Member.Name: nameof(Clock.Now) } clock => clock.Member.DeclaringType == typeof(Clock),
        MemberExpression { Expression: null, Member.Name: nameof(DateTimeOffset.UtcNow) } utc =>
            utc.Member.DeclaringType == typeof(DateTimeOffset) || utc.Member.DeclaringType == typeof(DateTime),
        MemberExpression { Member.Name: nameof(DateTimeOffset.UtcDateTime), Expression: { } moment } => IsNow(moment),
        _ => false,
    };
}
