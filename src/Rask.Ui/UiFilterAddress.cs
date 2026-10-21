using System.Collections;
using System.Reflection;
using Rask.Core.Forms;
using Rask.Core.Routing;

namespace Rask;

/// <summary>How a filter's value is kept in the address, and when it is a filter at all.</summary>
internal static class UiFilterAddress
{
    /// <summary>The query key of a page member: its <c>[QueryParam]</c> name, or the member's own name.</summary>
    /// <exception cref="InvalidOperationException">The member is no query parameter.</exception>
    internal static string Key(PropertyInfo member) =>
        member.GetCustomAttribute<QueryParamAttribute>() is { } query
            ? query.Name ?? member.Name
            : throw new InvalidOperationException(
                $"Ui.Filters keeps every filter in the address, so {member.DeclaringType?.Name}.{member.Name} has to be "
                + $"filled from it. Declare it as a query parameter: [QueryParam(\"{Camel(member.Name)}\")] public "
                + $"{TypeName(member.PropertyType)} {member.Name} {{ get; set; }}");

    /// <summary>Whether <paramref name="value" /> is a filter in force: set, not its type's default, not blank, not empty.</summary>
    internal static bool Holds<T>(T? value) => value switch
    {
        null => false,
        bool on => on,
        string text => !string.IsNullOrWhiteSpace(text),
        ICollection items => items.Count > 0,
        _ => !EqualityComparer<T>.Default.Equals(value, default),
    };

    /// <summary>The query value for <paramref name="value" />; nothing, which takes the key out, when it is no filter.</summary>
    internal static string? Write<T>(T? value) => value switch
    {
        _ when !Holds(value) => null,
        bool => "true",
        _ => BindingHelpers.FormatValue(value),
    };

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    // As the declaration writes it: the keyword, and a question mark where the member may be unset.
    private static string TypeName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } plain)
        {
            return TypeName(plain) + "?";
        }

        return Type.GetTypeCode(type) switch
        {
            TypeCode.String => "string?",
            TypeCode.Boolean => "bool",
            TypeCode.Int32 => "int",
            TypeCode.Int64 => "long",
            TypeCode.Decimal => "decimal",
            TypeCode.Double => "double",
            _ => type.Name,
        };
    }
}
