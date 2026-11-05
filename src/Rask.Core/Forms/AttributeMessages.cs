using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Rask.Core.Globalization;

namespace Rask.Core.Forms;

// What a built-in DataAnnotations attribute says when it fails and was given no words of its own, in the reader's
// language. .NET writes those messages in English only — it ships no satellite resources for them — so under a
// Hungarian culture a bare [Required] would say "The Name field is required.". This takes the sentence from the
// RaskStrings catalog instead (the app's, then a library's: the kit's Hungarian), and keeps .NET's own text where
// neither has one, which is every English reader: an English page reads exactly what it read before.
//
// An attribute's own ErrorMessage, or its ErrorMessageResourceType, is the app's word, and always wins.
internal static class AttributeMessages
{
    // What .NET puts in ErrorMessage itself, for the attributes that expose their default there rather than keep it
    // out of sight: such an ErrorMessage is no word of the app's.
    private static readonly Dictionary<Type, string?> _defaults = new()
    {
        [typeof(EmailAddressAttribute)] = new EmailAddressAttribute().ErrorMessage,
        [typeof(PhoneAttribute)] = new PhoneAttribute().ErrorMessage,
        [typeof(UrlAttribute)] = new UrlAttribute().ErrorMessage,
        [typeof(CreditCardAttribute)] = new CreditCardAttribute().ErrorMessage,
        [typeof(FileExtensionsAttribute)] = new FileExtensionsAttribute().ErrorMessage,
        [typeof(AllowedValuesAttribute)] = new AllowedValuesAttribute().ErrorMessage,
        [typeof(DeniedValuesAttribute)] = new DeniedValuesAttribute().ErrorMessage,
        [typeof(Base64StringAttribute)] = new Base64StringAttribute().ErrorMessage,
    };

    // A member's built-in attributes that carry no words of their own — the ones whose failure this may reword — or
    // null for a member with none. Bounded by the properties of the models an app validates.
    private static readonly ConcurrentDictionary<(Type Owner, string Member), Rewordable?> _rewordable = new();

    /// <summary>
    ///     The catalog's sentence for <paramref name="check" /> failing on <paramref name="value" />, or null to
    ///     keep .NET's: an attribute with words of its own, one Rask has no key for, or a language with no
    ///     translation.
    /// </summary>
    internal static string? For(ValidationAttribute check, object? value)
    {
        if (!IsBuiltIn(check) || HasOwnWords(check))
        {
            return null;
        }

        var text = value is string;
        return check switch
        {
            RequiredAttribute => Said(RaskString.ValidationRequired, "This field is required."),
            StringLengthAttribute { MinimumLength: > 0 } length =>
                Said(RaskString.ValidationLength, "Enter between {0} and {1} characters.", length.MinimumLength, length.MaximumLength),
            StringLengthAttribute length => Said(RaskString.ValidationTooLong, "Enter at most {0} characters.", length.MaximumLength),
            MaxLengthAttribute most when text => Said(RaskString.ValidationTooLong, "Enter at most {0} characters.", most.Length),
            MaxLengthAttribute most => Said(RaskString.ValidationTooMany, "Choose at most {0}.", most.Length),
            MinLengthAttribute least when text => Said(RaskString.ValidationTooShort, "Enter at least {0} characters.", least.Length),
            MinLengthAttribute least => Said(RaskString.ValidationTooFew, "Choose at least {0}.", least.Length),
            LengthAttribute length when text =>
                Said(RaskString.ValidationLength, "Enter between {0} and {1} characters.", length.MinimumLength, length.MaximumLength),
            LengthAttribute length => Said(RaskString.ValidationCount, "Choose between {0} and {1}.", length.MinimumLength, length.MaximumLength),

            // An exclusive bound reads differently ("more than"), and no key says that yet: .NET's text stands.
            RangeAttribute { MinimumIsExclusive: false, MaximumIsExclusive: false } range =>
                Said(RaskString.ValidationBetween, "Enter a value between {0} and {1}.", range.Minimum, range.Maximum),
            EmailAddressAttribute => Said(RaskString.ValidationEmail, "Enter a valid email address."),
            PhoneAttribute => Said(RaskString.ValidationPhone, "Enter a valid phone number."),
            UrlAttribute => Said(RaskString.ValidationUrl, "Enter a valid http, https or ftp address."),
            CreditCardAttribute => Said(RaskString.ValidationCreditCard, "Enter a valid card number."),
            RegularExpressionAttribute => Said(RaskString.ValidationPattern, "Enter a value in the expected format."),
            CompareAttribute other => Said(RaskString.ValidationMatch, "Must match {0}.", other.OtherPropertyDisplayName ?? other.OtherProperty),
            FileExtensionsAttribute files => Said(RaskString.ValidationFileType, "Choose a file of type {0}.", Listed(files.Extensions)),
            AllowedValuesAttribute or DeniedValuesAttribute => Said(RaskString.ValidationNotAllowed, "This value is not allowed."),
            Base64StringAttribute => Said(RaskString.ValidationBase64, "Enter valid Base64 text."),
            EnumDataTypeAttribute => Said(RaskString.ValidationInvalid, "Enter a valid value."),
            _ => null,
        };
    }

    /// <summary>
    ///     <paramref name="said" />, a failure <see cref="Validator" /> reported for <paramref name="member" /> of
    ///     <paramref name="owner" />, in the reader's language where it is a built-in attribute's own text.
    /// </summary>
    /// <remarks>
    ///     A <see cref="ValidationResult" /> does not say which attribute wrote it, so each of the member's built-in
    ///     attributes is asked again, and the one that writes the same words is the one that failed. Only where the
    ///     catalog has a sentence for it: an English reader costs one lookup per attribute and nothing else.
    /// </remarks>
    internal static string Reworded(string said, object owner, string member, IServiceProvider? services)
    {
        if (member.Length == 0 || _rewordable.GetOrAdd((owner.GetType(), member), Find) is not { } found)
        {
            return said;
        }

        var value = found.Property.GetValue(owner);
        foreach (var check in found.Checks)
        {
            // Asked first, so a language with no translation never runs a rule twice.
            if (For(check, value) is null || !Wrote(check, said, value, owner, member, services))
            {
                continue;
            }

            // Asked again: a [Compare] learns the other field's display name while it runs.
            return For(check, value) ?? said;
        }

        return said;
    }

    // An ErrorMessage the app wrote, or a resource it named. Not the default .NET exposes as ErrorMessage itself.
    private static bool HasOwnWords(ValidationAttribute check) =>
        check.ErrorMessageResourceType is not null
        || check.ErrorMessageResourceName is not null
        || (check.ErrorMessage is { } message
            && !(_defaults.TryGetValue(check.GetType(), out var standard) && string.Equals(message, standard, StringComparison.Ordinal)));

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Builds the ValidationContext the attribute already ran under, on the model Validator just " +
                        "reflected over: the app preserves the models its forms validate (DataAnnotationsFieldValidator).")]
    private static bool Wrote(ValidationAttribute check, string said, object? value, object owner, string member, IServiceProvider? services)
    {
        var context = new ValidationContext(owner, services, null) { MemberName = member };
        return string.Equals(check.GetValidationResult(value, context)?.ErrorMessage, said, StringComparison.Ordinal);
    }

    // .NET's own, not a subclass an app wrote: what an app derives may word its failure any way it likes.
    private static bool IsBuiltIn(ValidationAttribute check) => check.GetType().Assembly == typeof(ValidationAttribute).Assembly;

    // The member's property and its rewordable attributes, or null when it has none: looked up once per member.
    private static Rewordable? Find((Type Owner, string Member) field)
    {
        if (RequiredFields.Property(field.Owner, field.Member) is not { } property)
        {
            return null;
        }

        ValidationAttribute[] checks =
            [.. property.GetCustomAttributes<ValidationAttribute>(inherit: true).Where(static check => IsBuiltIn(check) && !HasOwnWords(check))];
        return checks.Length == 0 ? null : new Rewordable(property, checks);
    }

    // "png,jpg" as .NET lists it: ".png, .jpg".
    private static string Listed(string extensions) =>
        string.Join(", ", extensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static extension => extension.StartsWith('.') ? extension : "." + extension));

    private static string? Said(RaskString key, string english) => RaskStrings.Translates(key) ? RaskStrings.Get(key, english) : null;

    private static string? Said<T0>(RaskString key, string english, T0 arg0) =>
        RaskStrings.Translates(key) ? RaskStrings.Get(key, english, arg0) : null;

    private static string? Said<T0, T1>(RaskString key, string english, T0 arg0, T1 arg1) =>
        RaskStrings.Translates(key) ? RaskStrings.Get(key, english, arg0, arg1) : null;

    private sealed record Rewordable(PropertyInfo Property, ValidationAttribute[] Checks);
}
