using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     Writes what <c>Default(…)</c> and <c>StoredDefault(…)</c> declared as the provider's own default SQL — a literal, the
///     database's clock in UTC, or its wall clock — and forgets the declaration.
/// </summary>
/// <remarks>
///     Every default is SQL (<c>HasDefaultValueSql</c>), never EF Core's <c>HasDefaultValue</c>: the model a migration is
///     made from then says exactly what a hand-written <c>HasDefaultValueSql("1000")</c> said, so moving to
///     <c>Default(x =&gt; x.Size, 1000)</c> changes no column.
/// </remarks>
/// <param name="sql">The provider's dialect, or null for a provider that writes no SQL.</param>
internal sealed class DatabaseDefaultConvention(ModelSql? sql) : IModelFinalizingConvention
{
    /// <summary>The provider-neutral declaration, a <see cref="DeclaredDefault" />.</summary>
    internal const string Annotation = "Rask:Default";

    /// <summary>Reads the clock a lambda default names, refusing anything but the database's two.</summary>
    /// <param name="now">The lambda's body.</param>
    /// <param name="declaration">The declaration, for the refusal to name.</param>
    /// <exception cref="ArgumentException">It is something else.</exception>
    internal static DeclaredDefault Clock(Expression now, string declaration)
    {
        if (now is MemberExpression { Member.Name: nameof(Rask.Clock.Wall), Expression: null } wall
            && wall.Member.DeclaringType == typeof(Rask.Clock))
        {
            return DeclaredDefault.Wall;
        }

        if (now is MemberExpression { Member.Name: nameof(Rask.Clock.Today), Expression: null } today
            && today.Member.DeclaringType == typeof(Rask.Clock))
        {
            throw new ArgumentException(
                $"{declaration}: the database has no calendar day of its own a column could default to. Default to "
                + "() => Clock.Wall, the database's wall clock, or () => Clock.Now, a moment in UTC.",
                nameof(now));
        }

        if (!IsNow(now))
        {
            throw new ArgumentException(
                $"{declaration}: a lambda default is the database's clock, () => Clock.Now (or () => Clock.Now.UtcDateTime "
                + "for a DateTime), or its wall clock, () => Clock.Wall. A constant goes as itself: Default(x => x.Size, 1000).",
                nameof(now));
        }

        var type = Nullable.GetUnderlyingType(now.Type) ?? now.Type;
        return type == typeof(DateTimeOffset) ? DeclaredDefault.NowOffset : DeclaredDefault.NowUtc;
    }

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var property in entityType.GetDeclaredProperties())
            {
                if (property.FindAnnotation(Annotation)?.Value is not DeclaredDefault declared)
                {
                    continue;
                }

                property.RemoveAnnotation(Annotation);
                if (sql is not null)
                {
                    property.SetDefaultValueSql(Sql(property, declared, sql), fromDataAnnotation: true);
                }
            }
        }
    }

    private static string Sql(IConventionProperty property, DeclaredDefault declared, ModelSql sql)
    {
        var mapping = sql.Mapping(property);
        var stored = Stored(property, mapping);
        var declaration = declared.Describe(property.Name);
        sql.Supported(declaration);

        switch (declared.Kind)
        {
            case DefaultKind.Value:
                return sql.Default(declared.Value, mapping, stored: false);

            case DefaultKind.Stored:
                var given = Nullable.GetUnderlyingType(declared.Value!.GetType()) ?? declared.Value.GetType();
                if (given != stored)
                {
                    throw new InvalidOperationException(
                        $"{declaration}: the column of {property.DeclaringType.DisplayName()}.{property.Name} stores a "
                        + $"{stored.Name}, and the default is a {given.Name}. Give the value as the column stores it.");
                }

                return sql.Default(declared.Value, mapping, stored: true);

            case DefaultKind.Wall:
                if (stored != typeof(DateTime))
                {
                    throw new InvalidOperationException(
                        $"{declaration}: a wall-clock time has no offset, so it goes into a DateTime column (or a value "
                        + $"object over one); {property.DeclaringType.DisplayName()}.{property.Name} stores a {stored.Name}. "
                        + "A DateTimeOffset defaults to () => Clock.Now.");
                }

                return sql.Wall();

            default:
                var clock = declared.Kind == DefaultKind.NowOffset ? typeof(DateTimeOffset) : typeof(DateTime);
                if (stored != clock)
                {
                    throw new InvalidOperationException(
                        $"{declaration}: {property.DeclaringType.DisplayName()}.{property.Name} stores a {stored.Name}. "
                        + "A DateTimeOffset defaults to () => Clock.Now, a DateTime to () => Clock.Now.UtcDateTime.");
                }

                return sql.Now(clock, property.GetColumnType() ?? mapping?.StoreType);
        }
    }

    // What the column holds: a value object's (or an enum's) converted type, the property's own otherwise.
    private static Type Stored(IConventionProperty property, RelationalTypeMapping? mapping)
    {
        var type = (property.GetValueConverter() ?? mapping?.Converter)?.ProviderClrType ?? property.ClrType;
        type = Nullable.GetUnderlyingType(type) ?? type;

        // An enum with no converter of its own is stored as its number, by the provider's built-in conversion.
        return type.IsEnum ? Enum.GetUnderlyingType(type) : type;
    }

    // Clock.Now, Clock.Now.UtcDateTime, DateTimeOffset.UtcNow, DateTime.UtcNow — and nothing that reads another moment.
    private static bool IsNow(Expression now) => now switch
    {
        UnaryExpression { NodeType: ExpressionType.Convert } lifted => IsNow(lifted.Operand),
        MemberExpression { Expression: null, Member.Name: nameof(Rask.Clock.Now) } clock => clock.Member.DeclaringType == typeof(Rask.Clock),
        MemberExpression { Expression: null, Member.Name: nameof(DateTimeOffset.UtcNow) } utc =>
            utc.Member.DeclaringType == typeof(DateTimeOffset) || utc.Member.DeclaringType == typeof(DateTime),
        MemberExpression { Member.Name: nameof(DateTimeOffset.UtcDateTime), Expression: { } moment } => IsNow(moment),
        _ => false,
    };
}
