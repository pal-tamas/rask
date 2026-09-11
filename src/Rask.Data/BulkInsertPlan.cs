using System.Collections.Concurrent;
using System.Data;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.Data;

/// <summary>
/// The prepared <c>INSERT</c> for one entity type: the statement text, and for each mapped column the
/// accessor that reads it off an entity plus the conversion the provider expects. Built once per
/// (model, entity type) and reused — building it is reflection, and the point of the path it serves is to
/// hoist every per-row cost that can be hoisted.
/// </summary>
internal sealed class BulkInsertPlan
{
    // Keyed by the model, so a context configured differently gets its own plan and a collected model takes
    // its entry with it. Models are singletons per configuration, so this stays tiny.
    private static readonly ConditionalWeakTable<IModel, ConcurrentDictionary<Type, BulkInsertPlan>> Cache = new();

    private BulkInsertPlan(string commandText, IReadOnlyList<BulkInsertColumn> columns, BulkTimestamps? timestamps)
    {
        CommandText = commandText;
        Columns = columns;
        Timestamps = timestamps;
    }

    /// <summary>The single-row <c>INSERT</c>, with one named parameter per column.</summary>
    internal string CommandText { get; }

    /// <summary>The mapped columns, in the order their parameters appear.</summary>
    internal IReadOnlyList<BulkInsertColumn> Columns { get; }

    /// <summary>Setters for the audit stamps, when the entity is <see cref="ITimestamped"/>.</summary>
    internal BulkTimestamps? Timestamps { get; }

    internal static BulkInsertPlan For<TEntity>(DbContext context)
        where TEntity : class
    {
        var perModel = Cache.GetValue(context.Model, static _ => new ConcurrentDictionary<Type, BulkInsertPlan>());
        return perModel.GetOrAdd(typeof(TEntity), _ => Build<TEntity>(context));
    }

    private static BulkInsertPlan Build<TEntity>(DbContext context)
        where TEntity : class
    {
        var entityType = context.Model.FindEntityType(typeof(TEntity))
            ?? throw Unsupported($"{typeof(TEntity).Name} is not mapped on this context.");

        if (StoreObjectIdentifier.Create(entityType, StoreObjectType.Table) is not { } storeObject)
        {
            throw Unsupported($"{typeof(TEntity).Name} does not map to a plain table.");
        }

        if (entityType.BaseType is not null || entityType.GetDirectlyDerivedTypes().Any())
        {
            throw Unsupported(
                $"{typeof(TEntity).Name} takes part in an inheritance hierarchy, whose discriminator and " +
                "shared table this writer does not build.");
        }

        if (entityType.GetNavigations().Any() || entityType.GetSkipNavigations().Any())
        {
            throw Unsupported(
                $"{typeof(TEntity).Name} has navigations, and nothing walks the graph on this path — related " +
                "rows would be silently dropped.");
        }

        // The provider's own spelling of identifiers and parameters. A hand-written "…" works on SQLite,
        // PostgreSQL and SQL Server but is a string literal to MySQL, whose identifiers are backtick-quoted;
        // asking the provider is the only spelling right on all of them.
        var sql = context.GetService<ISqlGenerationHelper>();

        var columns = new List<BulkInsertColumn>();
        foreach (var property in entityType.GetProperties())
        {
            if (property.GetColumnName(storeObject) is not { } columnName)
            {
                continue;
            }

            // Only values the STORE supplies are fatal — reading them back is exactly what the change tracker
            // is for. EF marks a Guid key ValueGenerated.OnAdd by convention even though the value is produced
            // on the client, and Rask entities set their own Id in a factory, so OnAdd alone must not
            // disqualify the most ordinary entity there is.
            if (property.GetComputedColumnSql() is not null || property.GetDefaultValueSql() is not null)
            {
                throw Unsupported(
                    $"{typeof(TEntity).Name}.{property.Name} is computed by the store, and reading generated " +
                    "values back is exactly what the change tracker is for.");
            }

            if (property.ValueGenerated == ValueGenerated.OnAdd
                && property.IsPrimaryKey()
                && IsIntegral(property.ClrType))
            {
                throw Unsupported(
                    $"{typeof(TEntity).Name}.{property.Name} is a store-assigned integer key, whose value only " +
                    "exists after the insert the change tracker reads it back from.");
            }

            if (property.IsShadowProperty())
            {
                throw Unsupported(
                    $"{typeof(TEntity).Name} has the shadow property '{property.Name}', which only the change " +
                    "tracker can supply a value for.");
            }

            var mapping = property.GetTypeMapping();
            var parameter = $"p{columns.Count}";
            columns.Add(new BulkInsertColumn(
                columnName,
                sql.GenerateParameterName(parameter),
                sql.GenerateParameterNamePlaceholder(parameter),
                BuildGetter<TEntity>(property),
                mapping.Converter,
                (mapping as RelationalTypeMapping)?.DbType,
                // Nothing generates values on this path, so a client-generated key left at its default would
                // be written as-is - and the second such row would collide on the primary key.
                property.ValueGenerated != ValueGenerated.Never ? $"{typeof(TEntity).Name}.{property.Name}" : null,
                GetDefault(property.ClrType)));
        }

        if (columns.Count == 0)
        {
            throw Unsupported($"{typeof(TEntity).Name} maps no columns.");
        }

        var table = sql.DelimitIdentifier(entityType.GetTableName()!, entityType.GetSchema());

        var text =
            $"INSERT INTO {table} (" +
            string.Join(", ", columns.Select(c => sql.DelimitIdentifier(c.ColumnName))) +
            ") VALUES (" +
            string.Join(", ", columns.Select(static c => c.ParameterPlaceholder)) +
            ");";

        return new BulkInsertPlan(text, columns, BuildTimestamps<TEntity>(entityType));
    }

    private static BulkTimestamps? BuildTimestamps<TEntity>(IEntityType entityType)
        where TEntity : class
    {
        if (!typeof(ITimestamped).IsAssignableFrom(typeof(TEntity)))
        {
            return null;
        }

        var createdAt = entityType.FindProperty(Rask.Data.Columns.CreatedAt);
        var updatedAt = entityType.FindProperty(Rask.Data.Columns.UpdatedAt);

        return createdAt is null || updatedAt is null
            ? null
            : new BulkTimestamps(BuildSetter<TEntity>(createdAt), BuildSetter<TEntity>(updatedAt));
    }

    // The framework owns CreatedAt/UpdatedAt, so their CLR setters are deliberately not public. EF exposes the
    // backing field it maps, which is the same door AuditingInterceptor goes through when it writes the entry.
    private static Action<object, DateTime> BuildSetter<TEntity>(IProperty property)
        where TEntity : class
    {
        var entity = Expression.Parameter(typeof(object), "e");
        var value = Expression.Parameter(typeof(DateTime), "v");
        var typed = Expression.Convert(entity, typeof(TEntity));

        Expression target = property.FieldInfo is { } field
            ? Expression.Field(typed, field)
            : Expression.Property(typed, property.PropertyInfo
                ?? throw Unsupported($"{property.Name} has neither a backing field nor a property to write."));

        return Expression.Lambda<Action<object, DateTime>>(Expression.Assign(target, value), entity, value).Compile();
    }

    private static Func<object, object?> BuildGetter<TEntity>(IProperty property)
        where TEntity : class
    {
        var entity = Expression.Parameter(typeof(object), "e");
        var typed = Expression.Convert(entity, typeof(TEntity));

        Expression read = property.PropertyInfo is { } info
            ? Expression.Property(typed, info)
            : Expression.Field(typed, property.FieldInfo
                ?? throw Unsupported($"{property.Name} has neither a property nor a backing field to read."));

        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(read, typeof(object)), entity).Compile();
    }

    private static bool IsIntegral(Type type) =>
        Type.GetTypeCode(Nullable.GetUnderlyingType(type) ?? type)
            is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16
            or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;

    private static object? GetDefault(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;

    internal static InvalidOperationException Unsupported(string reason) =>
        new($"BulkInsertAsync cannot skip change tracking here: {reason} Drop SkipChangeTracking to insert " +
            "through the change tracker instead.");
}

/// <summary>One mapped column of a <see cref="BulkInsertPlan"/>.</summary>
/// <param name="ColumnName">The mapped column, undelimited.</param>
/// <param name="ParameterName">The name the <c>DbParameter</c> is bound under.</param>
/// <param name="ParameterPlaceholder">How the statement text refers to that parameter.</param>
/// <param name="Read">Reads the property off an entity.</param>
/// <param name="Converter">The value converter the provider stores through, when there is one.</param>
/// <param name="DbType">The parameter's <see cref="System.Data.DbType"/>, when the mapping names one.</param>
/// <param name="GeneratedName">The <c>Entity.Property</c> name of a value-generated column, for the error.</param>
/// <param name="ClrDefault">The CLR default a value-generated column must not still hold.</param>
internal sealed record BulkInsertColumn(
    string ColumnName,
    string ParameterName,
    string ParameterPlaceholder,
    Func<object, object?> Read,
    ValueConverter? Converter,
    DbType? DbType,
    string? GeneratedName,
    object? ClrDefault)
{
    /// <summary>Reads the column off <paramref name="entity"/> in the form the provider stores.</summary>
    internal object? ValueFor(object entity)
    {
        var value = Read(entity);

        // EF would have filled a value-generated property before the insert; this path has no one to do that,
        // so an unset one must be reported rather than written as a default that collides on the next row.
        if (GeneratedName is not null && Equals(value, ClrDefault))
        {
            throw BulkInsertPlan.Unsupported(
                $"{GeneratedName} is value-generated but still unset, and nothing generates values on this " +
                "path. Assign it before inserting.");
        }

        return value is null ? null : Converter is null ? value : Converter.ConvertToProvider(value);
    }
}

/// <summary>Setters for the audit stamps the writer applies in the interceptor's place.</summary>
internal sealed record BulkTimestamps(Action<object, DateTime> SetCreatedAt, Action<object, DateTime> SetUpdatedAt);
