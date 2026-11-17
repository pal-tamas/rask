using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;

namespace Rask.Core.Forms;

// What a model member is called where a reader sees it — a control's label, a filter's chip, the {0} of a validation
// message — read off its attributes as .NET 11's Blazor reads them: [Display]'s name (through its ResourceType when it
// names one) wins over [DisplayName], and a member with neither has no name of its own here, so each caller keeps
// its own fallback.
internal static class DisplayNames
{
    // A member's two attributes, read once. Not the name itself: one a ResourceType names is in the reader's language,
    // which a live session can change between two reads. Bounded by the members of the models an app binds.
    private static readonly ConcurrentDictionary<MemberInfo, Declaration> _declared = new();

    /// <summary>The name <paramref name="member" />'s <c>[Display]</c> or <c>[DisplayName]</c> gives it, or null.</summary>
    internal static string? Of(MemberInfo member)
    {
        var declared = _declared.GetOrAdd(member, Read);

        return declared.Display?.GetName() is { Length: > 0 } displayed ? displayed : declared.Named;
    }

    /// <summary>The name the member <paramref name="bound" /> reads gives it, or null for one that has none.</summary>
    internal static string? Of(LambdaExpression? bound) =>
        Unwrapped(bound?.Body) is MemberExpression { Member: PropertyInfo or FieldInfo } member ? Of(member.Member) : null;

    /// <summary>
    ///     Whether <paramref name="member" />'s name comes from resources its <c>[Display]</c> names, so it is in the
    ///     reader's language already, and not a literal an app's own resources may translate.
    /// </summary>
    internal static bool FromResources(MemberInfo member) => _declared.GetOrAdd(member, Read).Display?.ResourceType is not null;

    /// <summary>
    ///     The name <paramref name="member" /> is given where .NET's <see cref="Validator" /> would not see it: a
    ///     <c>[DisplayName]</c> on a member with no <c>[Display]</c> name, which .NET ignores and Blazor does not.
    /// </summary>
    internal static string? UnseenByValidator(MemberInfo member)
    {
        var declared = _declared.GetOrAdd(member, Read);

        return declared.Display?.GetName() is { Length: > 0 } ? null : declared.Named;
    }

    private static Declaration Read(MemberInfo member) =>
        new(
            member.GetCustomAttribute<DisplayAttribute>(inherit: true),
            member.GetCustomAttribute<DisplayNameAttribute>(inherit: true)?.DisplayName is { Length: > 0 } named ? named : null);

    // `() => (object)m.Count` names m.Count as much as `() => m.Count` does.
    private static Expression? Unwrapped(Expression? body) =>
        body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert ? convert.Operand : body;

    private sealed record Declaration(DisplayAttribute? Display, string? Named);
}
