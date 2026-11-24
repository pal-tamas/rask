using FluentValidation;
using Rask.Core.Forms;

namespace Rask;

// FluentValidation names a field by its own rules — a rule's WithName, then its global DisplayNameResolver, then the
// property's name split at its capitals — and its default resolver reads no attribute. This puts [Display] behind
// whatever resolver is there, so "'Given name' must not be empty." reads the [Display] name the field's label and its
// DataAnnotations messages read (DisplayNames), and nothing else. A resolver the app sets
// itself replaces this one, and a rule's WithName still wins over both.
internal static class FluentDisplayNames
{
    private static int _installed;

    internal static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1)
        {
            return;
        }

        var previous = ValidatorOptions.Global.DisplayNameResolver;
        ValidatorOptions.Global.DisplayNameResolver = (type, member, expression) =>
            previous?.Invoke(type, member, expression) ?? NameOf(type, member);
    }

    // A value object's own Value is named blank: the field it is bound to has the label, and a sentence about it reads
    // without one (UnnamedValue).
    private static string? NameOf(Type type, System.Reflection.MemberInfo? member)
    {
        if (member is null)
        {
            return null;
        }

        return typeof(Rask.Values.Value).IsAssignableFrom(type) && string.Equals(member.Name, "Value", StringComparison.Ordinal)
            ? ""
            : DisplayNames.Of(member);
    }
}
