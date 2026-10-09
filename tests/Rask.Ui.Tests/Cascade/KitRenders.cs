using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Rask.UiTests.Cascade;

/// <summary>
///     Every kit component that takes a <c>Class</c>, rendered on its own with one: bare, and once more per
///     flag and per value of each enum prop, so a size or a variant that writes other utilities is seen too.
/// </summary>
internal static partial class KitRenders
{
    /// <summary>The class an app would write, standing in for any.</summary>
    public const string AppClass = "app-own";

    /// <summary>The kit's components that take a <c>Class</c>, generic ones closed over a string.</summary>
    public static IEnumerable<Type> Components() =>
        typeof(UiStylesheet).Assembly.GetExportedTypes()
            .Where(type => !type.IsAbstract && typeof(global::Rask.Core.Component).IsAssignableFrom(type) && ClassOf(type) is not null)
            .OrderBy(type => type.Name, StringComparer.Ordinal);

    /// <summary>The name a finding is filed under: the type's, without its arity.</summary>
    public static string Name(Type type) => type.Name.Split('`')[0];

    /// <summary>
    ///     The elements that carry the app's class in each render of <paramref name="type" />; none when it
    ///     cannot be rendered alone, or puts the class nowhere.
    /// </summary>
    public static List<StyledElement> Elements(Type type)
    {
        var seen = new Dictionary<string, StyledElement>(StringComparer.Ordinal);
        foreach (var html in Renders(type))
        {
            foreach (Match tag in StartTag().Matches(html))
            {
                var attributes = Attribute().Matches(tag.Groups[2].Value)
                    .ToDictionary(m => m.Groups[1].Value, m => WebUtility.HtmlDecode(m.Groups[2].Value), StringComparer.Ordinal);
                var classes = attributes.GetValueOrDefault("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
                if (classes.Remove(AppClass))
                {
                    seen[tag.Value] = new StyledElement(tag.Groups[1].Value, classes, attributes);
                }
            }
        }

        return [.. seen.Values];
    }

    private static IEnumerable<string> Renders(Type type)
    {
        if (Render(type, _ => { }) is not { } bare)
        {
            yield break;
        }

        yield return bare;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite && p.DeclaringType!.Assembly == type.Assembly))
        {
            var kind = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            var values = kind == typeof(bool) ? [true] : kind.IsEnum ? Enum.GetValues(kind).Cast<object>() : [];
            foreach (var value in values)
            {
                if (Render(type, component => prop.SetValue(component, value)) is { } html)
                {
                    yield return html;
                }
            }
        }
    }

    // A component that needs a service, a parent's scope or a required prop does not render alone: null.
    private static string? Render(Type type, Action<object> set)
    {
        try
        {
            var closed = type.IsGenericTypeDefinition
                ? type.MakeGenericType([.. type.GetGenericArguments().Select(_ => typeof(string))])
                : type;
            var component = (global::Rask.Core.Component)Activator.CreateInstance(closed)!;
            ClassOf(closed)!.SetValue(component, AppClass);
            set(component);
            return component.ToHtml();
        }
#pragma warning disable CA1031 // Whatever a component throws when rendered out of its place, the answer is the same: not seen.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static PropertyInfo? ClassOf(Type type) =>
        type.GetProperty("Class", BindingFlags.Public | BindingFlags.Instance) is { CanWrite: true } prop && prop.PropertyType == typeof(string)
            ? prop
            : null;

    [GeneratedRegex("""<([a-zA-Z][\w-]*)((?:\s+[^\s=>/]+(?:="[^"]*")?)*)\s*/?>""")]
    private static partial Regex StartTag();

    [GeneratedRegex("""([^\s=>/]+)(?:="([^"]*)")?""")]
    private static partial Regex Attribute();
}
