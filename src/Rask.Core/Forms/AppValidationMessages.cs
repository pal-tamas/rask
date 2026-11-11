using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Validation;
using Rask.Core.Globalization;

namespace Rask.Core.Forms;

// What a failed DataAnnotations attribute says in the app's OWN words: .NET 11's localized validation messages,
// asked the way .NET's own validation pipeline asks them, so one set of resources words a form, a dispatched
// request and a minimal API alike.
//
// The app opts in with the platform's two calls and nothing of Rask's — AddLocalization(), and AddValidation() with a
// LocalizerProvider naming its shared resources (docs/forms.md shows both). With no IStringLocalizerFactory registered this is one service lookup per failure and nothing else: the app
// never asked for it. The keys, in the order .NET 11's generated validators try them:
//
//     the attribute's own ErrorMessage, used as a key
//     {Type}_{Member}_{Attribute}_Error      SignUp_Email_RequiredAttribute_Error
//     {Type}_{Attribute}_Error               SignUp_RequiredAttribute_Error
//     {Attribute}_Error                      RequiredAttribute_Error
//
// and the template found is written by the attribute itself — ValidationAttribute.FormatMessage(template,
// displayName) — so {0} is the field's display name and a [Range]'s {1} and {2} are its bounds, exactly as .NET
// writes them. An attribute that names a resource of its own (ErrorMessageResourceType) keeps it, as in .NET.
internal static class AppValidationMessages
{
    // .NET's default when the app configured no LocalizerProvider: the resources named after the model.
    private static readonly Func<Type, IStringLocalizerFactory, IStringLocalizer> _byModel =
        static (type, factory) => factory.Create(type);

    /// <summary>
    ///     The app's sentence for <paramref name="check" /> failing on <paramref name="member" /> of an
    ///     <paramref name="owner" />, or null when the app has no localizer or no key for it.
    /// </summary>
    internal static string? For(ValidationAttribute check, Type owner, string? member, IServiceProvider? services)
    {
        if (check.ErrorMessageResourceType is not null
            || services?.GetService(typeof(IStringLocalizerFactory)) is not IStringLocalizerFactory factory)
        {
            return null;
        }

        var localizer = Localizer(owner, factory, services);

        // The reader's language, not the thread's: a live session's culture is its own, and the localizer
        // reads CurrentUICulture while the attribute formats its numbers with CurrentCulture.
        using var _ = ReaderCulture.Pin();
        if (Template(localizer, check, owner, member) is not { } template)
        {
            return null;
        }

        return check.FormatMessage(template, DisplayName(owner, member, localizer));
    }

    private static IStringLocalizer Localizer(Type owner, IStringLocalizerFactory factory, IServiceProvider services)
    {
        var provider = (services.GetService(typeof(IOptions<ValidationOptions>)) as IOptions<ValidationOptions>)?.Value.LocalizerProvider
                       ?? _byModel;
        return provider(owner, factory)
               ?? throw new InvalidOperationException(
                   $"The ValidationOptions.LocalizerProvider delegate returned null for type '{owner.FullName}'. " +
                   "The delegate must return a non-null IStringLocalizer instance.");
    }

    // .NET 11's lookup order, key for key.
    private static string? Template(IStringLocalizer localizer, ValidationAttribute check, Type owner, string? member)
    {
        if (!string.IsNullOrEmpty(check.ErrorMessage) && Found(localizer[check.ErrorMessage]) is { } own)
        {
            return own;
        }

        var attribute = check.GetType().Name;
        var type = KeySegment(owner);
        if (!string.IsNullOrEmpty(member) && Found(localizer[$"{type}_{member}_{attribute}_Error"]) is { } byMember)
        {
            return byMember;
        }

        return Found(localizer[$"{type}_{attribute}_Error"]) ?? Found(localizer[$"{attribute}_Error"]);
    }

    private static string? Found(LocalizedString text) => text.ResourceNotFound ? null : text.Value;

    // "Order`1" is "Order", and a nullable is the type it wraps: the segment .NET writes into its keys.
    private static string KeySegment(Type type)
    {
        var name = (Nullable.GetUnderlyingType(type) ?? type).Name;
        var arity = name.IndexOf('`', StringComparison.Ordinal);
        return arity < 0 ? name : name[..arity];
    }

    // What {0} says: a [Display] or [DisplayName] the member carries — a literal one looked up in the same resources,
    // as .NET does — else the member's own name. A type-level rule names the type.
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "GetProperty on the type of a model the app validates: the app preserves its models' " +
                        "public properties for validation (DataAnnotationsFieldValidator).")]
    private static string DisplayName(Type owner, string? member, IStringLocalizer localizer)
    {
        if (string.IsNullOrEmpty(member))
        {
            return owner.Name;
        }

        var property = owner.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetCustomAttribute<DisplayAttribute>(inherit: true) is { } display)
        {
            return display.ResourceType is null && display.Name is { Length: > 0 } literal
                ? Found(localizer[literal]) ?? literal
                : display.GetName() ?? member;
        }

        if (property?.GetCustomAttribute<DisplayNameAttribute>(inherit: true)?.DisplayName is { Length: > 0 } named)
        {
            return Found(localizer[named]) ?? named;
        }

        return member;
    }

    // Pins the thread to the reader's culture for one synchronous lookup, and puts back what was there.
    private readonly struct ReaderCulture : IDisposable
    {
        private readonly CultureInfo? _culture;
        private readonly CultureInfo? _uiCulture;

        private ReaderCulture(CultureInfo? culture, CultureInfo? uiCulture)
        {
            _culture = culture;
            _uiCulture = uiCulture;
        }

        internal static ReaderCulture Pin()
        {
            var culture = RaskCulture.Current;
            var uiCulture = RaskCulture.CurrentUI;
            var previous = CultureInfo.CurrentCulture;
            var previousUi = CultureInfo.CurrentUICulture;
            if (ReferenceEquals(culture, previous) && ReferenceEquals(uiCulture, previousUi))
            {
                return default;
            }

            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
            return new ReaderCulture(previous, previousUi);
        }

        public void Dispose()
        {
            if (_culture is not null)
            {
                CultureInfo.CurrentCulture = _culture;
            }

            if (_uiCulture is not null)
            {
                CultureInfo.CurrentUICulture = _uiCulture;
            }
        }
    }
}
