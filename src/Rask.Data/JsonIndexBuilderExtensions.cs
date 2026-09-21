using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>Declares an index on a value inside a JSON column.</summary>
public static class JsonIndexBuilderExtensions
{
    /// <summary>
    /// Indexes the value at <paramref name="path"/> inside a JSON column, so a <c>Where</c> on it stops reading every
    /// row: <c>builder.HasJsonIndex(p =&gt; p.Meta.Status)</c>.
    /// </summary>
    /// <param name="builder">The entity's builder.</param>
    /// <param name="path">
    /// A member chain from the entity, through a navigation mapped with <c>ToJson()</c>, to the value:
    /// <c>p =&gt; p.Meta.Status</c>, or deeper, <c>p =&gt; p.Meta.Address.City</c>.
    /// </param>
    /// <typeparam name="TEntity">The entity.</typeparam>
    /// <returns>The same builder, to chain.</returns>
    /// <remarks>
    /// <para>
    /// EF Core already translates a filter on a JSON path, and without an index SQLite answers it by reading every
    /// row. This declares an expression index whose expression is exactly the one EF writes for that path — an
    /// expression index is only used for a byte-identical expression — so the filter becomes an index search.
    /// Declare it once per path; declaring a path twice is harmless.
    /// </para>
    /// <para>
    /// It arrives through a migration of its own, like any index: adding, changing or removing a declaration is
    /// something <c>dotnet ef migrations add</c> sees. SQLite only for now — <c>AddRaskData</c> refuses to boot a
    /// context on another provider that declares one, rather than let the filter scan without saying so.
    /// </para>
    /// </remarks>
    public static EntityTypeBuilder<TEntity> HasJsonIndex<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, object?>> path)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(path);

        var members = MemberChain(path);
        var current = JsonIndexSpec.TryParse(builder.Metadata.FindAnnotation(JsonIndexSpec.AnnotationName)?.Value, out var spec)
            ? spec
            : new JsonIndexSpec([]);

        builder.HasAnnotation(JsonIndexSpec.AnnotationName, current.With(members).Serialize());
        return builder;
    }

    // `p => p.Meta.Status` → [Meta, Status]. A value-typed leaf arrives wrapped in a Convert to object.
    private static string[] MemberChain(LambdaExpression path)
    {
        var body = path.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : path.Body;
        var members = new List<string>();

        while (body is MemberExpression member)
        {
            members.Add(member.Member.Name);
            body = member.Expression!;
        }

        if (body != path.Parameters[0] || members.Count < 2)
        {
            throw new ArgumentException(
                $"'{path}' is not a path into a JSON column. Name the navigation mapped with ToJson() and then the "
                + "value inside it, as a chain of members from the entity: p => p.Meta.Status.",
                nameof(path));
        }

        members.Reverse();
        return [.. members];
    }
}
