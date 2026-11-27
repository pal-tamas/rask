using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Data;

namespace Rask.Postgres;

/// <summary>
///     Refuses, as a <c>UseRaskPostgres</c> model is built, a bare <see cref="DateTime" /> on an aggregate or an entity
///     (an <see cref="IEntity" />), or inside a value object it holds: Npgsql
///     maps it to <c>timestamptz</c> and refuses any value that is not UTC — a wall-clock time, <c>DateTime.Now</c>, a time
///     read from a file — only when a row is saved, long after the model was written. Rask's way to hold time is a
///     <c>Moment</c> (an instant, stored as UTC on every database) or a <c>Day</c> (a calendar day).
/// </summary>
/// <remarks>
///     A property the app gave a converter of its own (<c>HasConversion(…)</c>) says how it is stored, and is left alone;
///     so is a table that is no aggregate or entity of the domain — a row an import stages. No namespace is exempt: Rask's
///     own tables hold their times as a <c>Timestamp</c>, a moment, as an app's do.
/// </remarks>
internal static class BareDateTimeColumns
{
    /// <summary>Throws for the first bare <see cref="DateTime" /> property of the app's own in <paramref name="model" />.</summary>
    /// <exception cref="InvalidOperationException">The model holds one.</exception>
    internal static void Refuse(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes().Where(static e => typeof(IEntity).IsAssignableFrom(e.ClrType)))
        {
            Refuse(entityType);
        }
    }

    private static void Refuse(ITypeBase type)
    {
        foreach (var property in type.GetDeclaredProperties())
        {
            if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTime) &&
                property.GetValueConverter() is null &&
                property.GetProviderClrType() is null &&
                (property.PropertyInfo as MemberInfo ?? property.FieldInfo) is not null)
            {
                throw new InvalidOperationException(
                    $"{type.DisplayName()}.{property.Name} is a bare DateTime, which PostgreSQL stores only as UTC: Npgsql refuses "
                    + "a wall-clock time when the row is saved. Hold an instant as a Moment "
                    + "(`public sealed partial record FueledAt(DateTime Value) : Moment(Value);`, stored as UTC on every database) "
                    + "and a calendar day as a Day (`... (DateOnly Value) : Day(Value);`) — RASK147 points at each one. "
                    + "A column that must stay a DateTime says how it is stored with HasConversion(…).");
            }
        }

        foreach (var complex in type.GetDeclaredComplexProperties())
        {
            Refuse(complex.ComplexType);
        }
    }
}
