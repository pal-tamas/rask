using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;

namespace Rask.Core.Forms;

// What a model member is called where a reader sees it — a control's label, a filter's chip, the {0} of a validation
// message — read off its [Display] name (through its ResourceType when it names one), the one attribute .NET's
// Validator reads too. A member without one has no name of its own here, so each caller keeps its own fallback.
internal static class DisplayNames
{
    // A member's [Display], read once. Not the name itself: one a ResourceType names is in the reader's language,
    // which a live session can change between two reads. Bounded by the members of the models an app binds.
    private static readonly ConcurrentDictionary<MemberInfo, DisplayAttribute?> _declared = new();

    /// <summary>The name <paramref name="member" />'s <c>[Display]</c> gives it, or null.</summary>
    internal static string? Of(MemberInfo member) =>
        _declared.GetOrAdd(member, Read)?.GetName() is { Length: > 0 } displayed ? displayed : null;

    /// <summary>The name the member <paramref name="bound" /> reads gives it, or null for one that has none.</summary>
    internal static string? Of(LambdaExpression? bound) =>
        Unwrapped(bound?.Body) is MemberExpression { Member: PropertyInfo or FieldInfo } member ? Of(member.Member) : null;

    /// <summary>
    ///     Whether <paramref name="member" />'s name comes from resources its <c>[Display]</c> names, so it is in the
    ///     reader's language already, and not a literal an app's own resources may translate.
    /// </summary>
    internal static bool FromResources(MemberInfo member) => _declared.GetOrAdd(member, Read)?.ResourceType is not null;

    private static DisplayAttribute? Read(MemberInfo member) => member.GetCustomAttribute<DisplayAttribute>(inherit: true);

    // `() => (object)m.Count` names m.Count as much as `() => m.Count` does.
    private static Expression? Unwrapped(Expression? body) =>
        body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert ? convert.Operand : body;
}
