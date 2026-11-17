using FluentValidation;
using Rask.Core.Forms;

namespace Rask;

// FluentValidation names a field by its own rules — a rule's WithName, then its global DisplayNameResolver, then the
// property's name split at its capitals — and its default resolver reads no attribute. This puts the attributes
// behind whatever resolver is there, so "'Given name' must not be empty." reads the [Display] or [DisplayName] the
// field's label and its DataAnnotations messages read (DisplayNames), [Display] first. A resolver the app sets
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
            previous?.Invoke(type, member, expression) ?? (member is null ? null : DisplayNames.Of(member));
    }
}
