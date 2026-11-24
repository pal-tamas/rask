using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     Writes a C# lambda over an entity as the SQL of the provider the model is built for: a computed column's value,
///     or the predicate of a filtered index.
/// </summary>
/// <remarks>
///     <para>
///         A small, closed subset — the one that means the same on SQL Server, PostgreSQL and SQLite as it does in C#:
///         the entity's mapped members (a value object's too), constants and captured values, <c>??</c>,
///         <c>+ - * / %</c> on whole numbers and doubles, text joined with <c>+</c>, comparisons, <c>&amp;&amp;</c>,
///         <c>||</c>, <c>!</c>, <c>?:</c>, <c>HasValue</c>, <c>GetValueOrDefault</c> and <c>Math.Abs</c>. Anything else
///         fails the model build, naming the node.
///     </para>
///     <para>
///         C#'s null rules are kept: a comparison is true or false, never unknown — <c>a == b</c> is true for two
///         nulls, and <c>a != 5</c> for a null — and a null joined into text is no text, as in C#.
///     </para>
///     <para>
///         A filter is plainer still, because SQL Server's filtered index takes only <c>AND</c>ed comparisons of a
///         column with a constant: no <c>||</c>, no <c>!</c> but on a boolean column, and no two columns compared.
///     </para>
/// </remarks>
internal sealed class ModelExpressionSql
{
    private const string Subset =
        "Rask writes the entity's members, constants, ??, + - * / % on whole numbers and doubles, text joined with +, "
        + "comparisons, && || !, ?:, HasValue, GetValueOrDefault() and Math.Abs.";

    private const string FilterSubset =
        "A filter is its columns compared with constants, joined by &&: x => x.RevokedAt == null && x.Kind == Kind.Live, "
        + "and x => x.Active or x => !x.Active for a boolean column.";

    private readonly ModelSql _sql;
    private readonly IReadOnlyEntityType _entity;
    private readonly ParameterExpression _row;
    private readonly string _declaration;
    private readonly bool _filter;

    private ModelExpressionSql(ModelSql sql, IReadOnlyEntityType entity, LambdaExpression lambda, string declaration, bool filter)
    {
        _sql = sql;
        _entity = entity;
        _row = lambda.Parameters[0];
        _declaration = declaration;
        _filter = filter;
    }

    /// <summary>The SQL for the value <paramref name="lambda" /> computes from a row.</summary>
    /// <param name="sql">The provider's dialect.</param>
    /// <param name="entity">The entity the lambda reads.</param>
    /// <param name="lambda">The value, as the app wrote it.</param>
    /// <param name="declaration">The declaration, for a refusal to name.</param>
    /// <exception cref="InvalidOperationException">The lambda says something the subset does not.</exception>
    internal static string Value(ModelSql sql, IReadOnlyEntityType entity, LambdaExpression lambda, string declaration)
    {
        return new ModelExpressionSql(sql, entity, lambda, declaration, filter: false).Write(lambda.Body).Sql;
    }

    /// <summary>The SQL for the rows <paramref name="lambda" /> keeps, as a filtered index's predicate.</summary>
    /// <param name="sql">The provider's dialect.</param>
    /// <param name="entity">The entity the lambda reads.</param>
    /// <param name="lambda">The predicate, as the app wrote it.</param>
    /// <param name="declaration">The declaration, for a refusal to name.</param>
    /// <exception cref="InvalidOperationException">The lambda says something a filter cannot.</exception>
    internal static string Filter(ModelSql sql, IReadOnlyEntityType entity, LambdaExpression lambda, string declaration)
    {
        var writer = new ModelExpressionSql(sql, entity, lambda, declaration, filter: true);
        var terms = Terms(lambda.Body).ToList();
        var notNull = terms.Select(writer.NotNullColumn).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return string.Join(" AND ", terms.Select(term => writer.FilterTerm(term, notNull)));
    }

    // ---- values ----------------------------------------------------------------------------------------------------

    private Fragment Write(Expression node)
    {
        node = Unwrap(node);
        if (Constant(node, out var constant))
        {
            return new Fragment(_sql.Literal(constant), constant is null, null, node.Type);
        }

        return node switch
        {
            MemberExpression { Member.Name: nameof(Nullable<>.Value), Expression: { } instance }
                when Nullable.GetUnderlyingType(instance.Type) is not null =>
                throw Refused(node, "reads .Value, which C# throws on for a null and SQL does not: write ?? or GetValueOrDefault()"),
            MemberExpression member when Column(member) is { } column => column,
            BinaryExpression { NodeType: ExpressionType.Coalesce } coalesce => Coalesce(coalesce),
            BinaryExpression { NodeType: ExpressionType.Add } add when add.Type == typeof(string) => Text(add),
            MethodCallExpression { Method.Name: nameof(string.Concat) } call when call.Method.DeclaringType == typeof(string) => Text(call),
            BinaryExpression binary when Arithmetic(binary.NodeType) is { } op => Calculate(binary, op),
            UnaryExpression { NodeType: ExpressionType.Negate or ExpressionType.NegateChecked } negate => Negate(negate),
            ConditionalExpression conditional => Conditional(conditional),
            MethodCallExpression { Method.Name: nameof(Nullable<>.GetValueOrDefault), Object: { } instance } call
                when Nullable.GetUnderlyingType(instance.Type) is { } underlying => ValueOrDefault(call, instance, underlying),
            MethodCallExpression { Method.Name: nameof(Math.Abs) } call when call.Method.DeclaringType == typeof(Math) => Abs(call),
            _ when IsBoolean(node) => new Fragment(Boolean(node), Nullable: false, null, typeof(bool)),
            _ => throw Refused(node, "is not one Rask writes as SQL"),
        };
    }

    private Fragment Coalesce(BinaryExpression coalesce)
    {
        var left = Write(coalesce.Left);
        var right = Write(coalesce.Right);
        return new Fragment($"COALESCE({left.Sql}, {right.Sql})", right.Nullable, left.Mapping ?? right.Mapping, coalesce.Type);
    }

    private Fragment Text(Expression joined) =>
        new(_sql.Concat(Joined(joined).Select(TextPart)), Nullable: false, null, typeof(string));

    private Fragment Calculate(BinaryExpression binary, string op)
    {
        Number(binary);
        var left = Write(binary.Left);
        var right = Write(binary.Right);
        return new Fragment($"({left.Sql} {op} {right.Sql})", left.Nullable || right.Nullable, null, binary.Type);
    }

    private Fragment Negate(UnaryExpression negate)
    {
        Number(negate);
        var operand = Write(negate.Operand);
        return operand with { Sql = $"(-{operand.Sql})", Mapping = null };
    }

    private Fragment Conditional(ConditionalExpression conditional)
    {
        var then = Write(conditional.IfTrue);
        var otherwise = Write(conditional.IfFalse);
        return new Fragment(
            $"CASE WHEN {Predicate(conditional.Test)} THEN {then.Sql} ELSE {otherwise.Sql} END",
            then.Nullable || otherwise.Nullable,
            then.Mapping ?? otherwise.Mapping,
            conditional.Type);
    }

    private Fragment ValueOrDefault(MethodCallExpression call, Expression instance, Type underlying)
    {
        var value = Write(instance);
        var fallback = call.Arguments.Count == 1 ? Write(call.Arguments[0]).Sql : _sql.Literal(Zero(call, underlying));
        return new Fragment($"COALESCE({value.Sql}, {fallback})", Nullable: false, value.Mapping, call.Type);
    }

    private Fragment Abs(MethodCallExpression call)
    {
        var operand = Write(call.Arguments[0]);
        return operand with { Sql = $"ABS({operand.Sql})", Mapping = null };
    }

    // A comparison in a value's place: true or false, as the provider writes a boolean.
    private string Boolean(Expression node) =>
        $"CASE WHEN {Predicate(node)} THEN {_sql.Literal(true)} ELSE {_sql.Literal(false)} END";

    // One part of text being joined: text as it is, a whole number as C# writes it, a null as no text at all.
    private string TextPart(Expression node)
    {
        var inner = Unwrap(node);
        var type = Nullable.GetUnderlyingType(inner.Type) ?? inner.Type;
        if (type != typeof(string) && !IsWhole(type))
        {
            throw Refused(node, $"joins a {type.Name} into text, which each database writes its own way: join text and whole numbers");
        }

        var part = Write(inner);
        var text = type == typeof(string) ? part.Sql : _sql.Text(part.Sql);
        return part.Nullable ? $"COALESCE({text}, {_sql.Literal(string.Empty)})" : text;
    }

    private Fragment? Column(MemberExpression member)
    {
        var path = new Stack<MemberInfo>();
        Expression? at = member;
        while (at is MemberExpression step)
        {
            path.Push(step.Member);
            at = Unwrap(step.Expression!);
        }

        if (at != _row)
        {
            return null;
        }

        IReadOnlyTypeBase type = _entity;
        var nullable = false;
        while (path.Count > 1)
        {
            var name = path.Pop().Name;
            if (path.Count == 1 && type.FindProperty(name) is { } stored && HeldValue(stored, path.Peek(), member) is { } held)
            {
                return held with { Nullable = nullable || held.Nullable };
            }

            var complex = type.FindComplexProperty(name)
                          ?? throw Refused(member, $"reads {name}, which is not a value object {type.DisplayName()} maps");
            nullable |= complex.IsNullable;
            type = complex.ComplexType;
        }

        var last = path.Pop().Name;
        var property = type.FindProperty(last)
                       ?? throw Refused(member, $"reads {last}, which is not a property {type.DisplayName()} maps");
        return new Fragment(_sql.Column(property.GetColumnName()), nullable || property.IsNullable, _sql.Mapping(property), property.ClrType);
    }

    // `x.Number.Value` of a one-value value object stored in one column: that column, holding the value itself — so a
    // constant beside it is the value, written as the column stores it (through the provider's own conversion, a
    // wall-clock moment as UTC where only UTC is kept).
    private Fragment? HeldValue(IReadOnlyProperty property, MemberInfo member, MemberExpression node)
    {
        var converter = property.GetValueConverter() ?? property.FindTypeMapping()?.Converter;
        if (!OneValueConverters.StoresAs(converter, member))
        {
            return null;
        }

        var held = Nullable.GetUnderlyingType(node.Type) ?? node.Type;
        return new Fragment(
            _sql.Column(property.GetColumnName()),
            property.IsNullable,
            _sql.Held(held, property.GetColumnType(), OneValueConverters.StoredAs(converter)),
            node.Type);
    }

    // ---- predicates ------------------------------------------------------------------------------------------------

    // True exactly when C# says true, and false otherwise — never unknown, so NOT and CASE read it as C# does.
    private string Predicate(Expression node)
    {
        node = Unwrap(node);
        switch (node)
        {
            case BinaryExpression { NodeType: ExpressionType.AndAlso or ExpressionType.And } both when IsBoolean(both):
                return $"({Predicate(both.Left)} AND {Predicate(both.Right)})";
            case BinaryExpression { NodeType: ExpressionType.OrElse or ExpressionType.Or } either when IsBoolean(either):
                return $"({Predicate(either.Left)} OR {Predicate(either.Right)})";
            case UnaryExpression { NodeType: ExpressionType.Not } negation when IsBoolean(negation):
                return $"NOT {Predicate(negation.Operand)}";
            case BinaryExpression comparison when Comparison(comparison.NodeType) is { } op:
                return Compare(comparison, op);
            case MemberExpression { Member.Name: nameof(Nullable<>.HasValue), Expression: { } instance }
                when Nullable.GetUnderlyingType(instance.Type) is not null:
                return $"{Write(instance).Sql} IS NOT NULL";
            case MemberExpression member when member.Type == typeof(bool) && Column(member) is { } flag:
                return $"{flag.Sql} = {_sql.Literal(true)}";
            default:
                throw Refused(node, "is not a condition Rask writes as SQL");
        }
    }

    private string Compare(BinaryExpression comparison, string op)
    {
        if (NullTest(comparison) is { } test)
        {
            return test;
        }

        var (left, right) = Sides(comparison);
        var equality = comparison.NodeType is ExpressionType.Equal or ExpressionType.NotEqual;
        var same = $"{left.Sql} {(equality ? "=" : op)} {right.Sql}";
        if (!left.Nullable && !right.Nullable)
        {
            return comparison.NodeType == ExpressionType.NotEqual ? $"{left.Sql} <> {right.Sql}" : same;
        }

        string[] guards = [.. new[] { left, right }.Where(static f => f.Nullable).Select(static f => $"{f.Sql} IS NOT NULL")];
        var known = $"({string.Join(" AND ", guards)} AND {same})";
        if (!equality)
        {
            return known;
        }

        // Two nulls are equal in C#; one null and a value are not.
        var equal = left.Nullable && right.Nullable
            ? $"(({left.Sql} IS NULL AND {right.Sql} IS NULL) OR {known})"
            : known;
        return comparison.NodeType == ExpressionType.Equal ? equal : $"NOT {equal}";
    }

    // x == null, null != x: IS NULL and IS NOT NULL.
    private string? NullTest(BinaryExpression comparison)
    {
        if (comparison.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual))
        {
            return null;
        }

        if (NullSide(comparison) is not { } other)
        {
            return null;
        }

        return $"{Write(other).Sql} {(comparison.NodeType == ExpressionType.Equal ? "IS NULL" : "IS NOT NULL")}";
    }

    // Both sides, a constant written through the other side's column mapping — so an enum, a strongly-typed id or a
    // date is written as that column stores it.
    private (Fragment Left, Fragment Right) Sides(BinaryExpression comparison)
    {
        var leftNode = Unwrap(comparison.Left);
        var rightNode = Unwrap(comparison.Right);

        if (Constant(rightNode, out var rightValue) && !Constant(leftNode, out _))
        {
            var left = Write(leftNode);
            return (left, Against(rightValue, left));
        }

        if (Constant(leftNode, out var leftValue) && !Constant(rightNode, out _))
        {
            var right = Write(rightNode);
            return (Against(leftValue, right), right);
        }

        return (Write(leftNode), Write(rightNode));
    }

    private Fragment Against(object? value, Fragment column)
    {
        var typed = Coerce(value, column.Type);
        return new Fragment(_sql.Literal(typed, typed is null ? null : column.Mapping), typed is null, column.Mapping, column.Type);
    }

    // ---- filters ---------------------------------------------------------------------------------------------------

    private string FilterTerm(Expression term, HashSet<string> notNull)
    {
        term = Unwrap(term);
        switch (term)
        {
            case MemberExpression member when member.Type == typeof(bool) && Column(member) is { } flag:
                return $"{flag.Sql} = {_sql.FilterBoolean(true)}";
            case UnaryExpression { NodeType: ExpressionType.Not, Operand: var operand }
                when Unwrap(operand) is MemberExpression member && member.Type == typeof(bool) && Column(member) is { } flag:
                return $"{flag.Sql} = {_sql.FilterBoolean(false)}";
            case BinaryExpression comparison when Comparison(comparison.NodeType) is { } op:
                return FilterComparison(comparison, op, notNull);
            case BinaryExpression { NodeType: ExpressionType.OrElse or ExpressionType.Or }:
                throw Refused(term, "joins with ||, which SQL Server's filtered index cannot hold");
            default:
                throw Refused(term, "is not a filter Rask writes");
        }
    }

    private string FilterComparison(BinaryExpression comparison, string op, HashSet<string> notNull)
    {
        if (NullTest(comparison) is { } test)
        {
            return test;
        }

        var leftNode = Unwrap(comparison.Left);
        var rightNode = Unwrap(comparison.Right);
        var columnOnLeft = !Constant(leftNode, out _);
        if (columnOnLeft == !Constant(rightNode, out _) || (columnOnLeft ? leftNode : rightNode) is not MemberExpression)
        {
            throw Refused(comparison, "does not compare one column with a constant");
        }

        var (left, right) = Sides(comparison);
        var column = columnOnLeft ? left : right;
        if (comparison.NodeType == ExpressionType.NotEqual && column.Nullable && !notNull.Contains(column.Sql))
        {
            // C# keeps the rows with a null here, the database's index leaves them out.
            throw Refused(comparison, $"keeps the rows where the column is null in C#, which a filtered index cannot say. "
                                      + "Leave them out in both: add && x.Column != null");
        }

        return comparison.NodeType == ExpressionType.NotEqual ? $"{left.Sql} <> {right.Sql}" : $"{left.Sql} {op} {right.Sql}";
    }

    // The column a term says IS NOT NULL — "x.Kind != null" — which lets a "x.Kind != 3" beside it say what C# says.
    private string? NotNullColumn(Expression term) =>
        Unwrap(term) is BinaryExpression { NodeType: ExpressionType.NotEqual } comparison
        && NullSide(comparison) is { } other
        && Unwrap(other) is MemberExpression member
        && Column(member) is { } column
            ? column.Sql
            : null;

    private static IEnumerable<Expression> Terms(Expression node) =>
        Unwrap(node) is BinaryExpression { NodeType: ExpressionType.AndAlso or ExpressionType.And } both && IsBoolean(both)
            ? Terms(both.Left).Concat(Terms(both.Right))
            : [node];

    // ---- shapes ----------------------------------------------------------------------------------------------------

    // The parts of a + b + c over text, and of string.Concat(a, b, c).
    private static IEnumerable<Expression> Joined(Expression node) => Unwrap(node) switch
    {
        BinaryExpression { NodeType: ExpressionType.Add } add when add.Type == typeof(string) =>
            Joined(add.Left).Concat(Joined(add.Right)),
        MethodCallExpression { Method.Name: nameof(string.Concat), Arguments: [NewArrayExpression array] } call
            when call.Method.DeclaringType == typeof(string) => array.Expressions.SelectMany(Joined),
        MethodCallExpression { Method.Name: nameof(string.Concat) } call when call.Method.DeclaringType == typeof(string) =>
            call.Arguments.SelectMany(Joined),
        var part => [part],
    };

    // A numeric operation on whole numbers or doubles; a decimal is refused, since SQLite would compute it as a double.
    private void Number(Expression node)
    {
        var type = Nullable.GetUnderlyingType(node.Type) ?? node.Type;
        if (!IsWhole(type) && type != typeof(double) && type != typeof(float))
        {
            throw Refused(node, $"computes on {type.Name}, which SQLite would compute as a double: compute whole numbers or doubles");
        }
    }

    // A cast C# adds for its own reasons — boxing, lifting to nullable, widening a number, an enum to its number — is
    // no change to the value the database holds.
    private static Expression Unwrap(Expression node)
    {
        while (node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } cast
               && Transparent(cast.Operand.Type, cast.Type))
        {
            node = cast.Operand;
        }

        return node;
    }

    private static bool Transparent(Type from, Type to)
    {
        if (to == typeof(object))
        {
            return true;
        }

        var source = Nullable.GetUnderlyingType(from) ?? from;
        var target = Nullable.GetUnderlyingType(to) ?? to;
        if (source == target)
        {
            return true;
        }

        source = source.IsEnum ? Enum.GetUnderlyingType(source) : source;
        target = target.IsEnum ? Enum.GetUnderlyingType(target) : target;
        return source == target || (IsWhole(source) && (IsWhole(target) || target == typeof(double)));
    }

    // A constant, or a value captured from outside the lambda (a closure's field, a static readonly one) — read now,
    // as the model is built. A property is not read: DateTime.Now would be frozen at that moment.
    private bool Constant(Expression node, out object? value)
    {
        switch (Unwrap(node))
        {
            case ConstantExpression constant:
                value = constant.Value;
                return true;
            case MemberExpression { Member: FieldInfo field } member when member.Expression is null || Constant(member.Expression, out _):
                Constant(member.Expression ?? Expression.Constant(null), out var owner);
                value = field.GetValue(owner);
                return true;
            case MemberExpression { Member: PropertyInfo } member when Captured(member):
                throw Refused(node, "reads a property outside the row, which would be read once, as the model is built: "
                                    + "capture it in a field or write it as a constant");
            default:
                value = null;
                return false;
        }
    }

    private bool Captured(MemberExpression member)
    {
        Expression? at = member;
        while (at is MemberExpression step)
        {
            at = step.Expression is null ? null : Unwrap(step.Expression);
        }

        return at != _row;
    }

    // The side compared with null, in x == null and null == x.
    private static Expression? NullSide(BinaryExpression comparison)
    {
        if (IsNull(comparison.Right))
        {
            return comparison.Left;
        }

        return IsNull(comparison.Left) ? comparison.Right : null;
    }

    private static bool IsNull(Expression node) => Unwrap(node) is ConstantExpression { Value: null };

    private static bool IsBoolean(Expression node) => node.Type == typeof(bool);

    private static bool IsWhole(Type type) =>
        type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
        || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(uint);

    private static string? Arithmetic(ExpressionType type) => type switch
    {
        ExpressionType.Add or ExpressionType.AddChecked => "+",
        ExpressionType.Subtract or ExpressionType.SubtractChecked => "-",
        ExpressionType.Multiply or ExpressionType.MultiplyChecked => "*",
        ExpressionType.Divide => "/",
        ExpressionType.Modulo => "%",
        _ => null,
    };

    private static string? Comparison(ExpressionType type) => type switch
    {
        ExpressionType.Equal => "=",
        ExpressionType.NotEqual => "<>",
        ExpressionType.LessThan => "<",
        ExpressionType.LessThanOrEqual => "<=",
        ExpressionType.GreaterThan => ">",
        ExpressionType.GreaterThanOrEqual => ">=",
        _ => null,
    };

    // A constant C# typed as the comparison's (an enum's number, a widened int), back to the column's own type.
    private static object? Coerce(object? value, Type columnType)
    {
        var type = Nullable.GetUnderlyingType(columnType) ?? columnType;
        if (value is null || value.GetType() == type)
        {
            return value;
        }

        if (type.IsEnum)
        {
            return Enum.ToObject(type, value);
        }

        return value is IConvertible && typeof(IConvertible).IsAssignableFrom(type)
            ? Convert.ChangeType(value, type, CultureInfo.InvariantCulture)
            : value;
    }

    private InvalidOperationException Refused(Expression node, string why) =>
        new($"{_declaration} on {_entity.DisplayName()} cannot be written for the database: {node} {why}. "
            + (_filter ? FilterSubset : Subset));

    // GetValueOrDefault()'s default, for the types whose default a literal can say.
    private InvalidOperationException ZeroRefused(Expression node) =>
        Refused(node, "falls back to a default only a number, a boolean or an enum has as a literal: write ?? with the value");

    private object Zero(Expression node, Type type)
    {
        if (type.IsEnum)
        {
            return Enum.ToObject(type, 0);
        }

        return IsWhole(type) || type == typeof(double) || type == typeof(float) || type == typeof(decimal) || type == typeof(bool)
            ? Convert.ChangeType(0, type, CultureInfo.InvariantCulture)
            : throw ZeroRefused(node);
    }

    /// <summary>A piece of SQL, and what the C# it came from was.</summary>
    /// <param name="Sql">The SQL.</param>
    /// <param name="Nullable">Whether it can be NULL.</param>
    /// <param name="Mapping">The column's mapping, when it is (or stands for) one — how a constant beside it is written.</param>
    /// <param name="Type">The C# type.</param>
    private readonly record struct Fragment(string Sql, bool Nullable, RelationalTypeMapping? Mapping, Type Type);
}
