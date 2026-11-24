using System.Globalization;
using FluentValidation;
using FluentValidation.Resources;
using Rask.Core.Globalization;

namespace Rask;

// FluentValidation's default messages, in Rask's words. A built-in rule with no WithMessage of its own asks the global
// language manager for its sentence; this answers with the RaskStrings sentence the equivalent DataAnnotations
// attribute says (CatalogSentences), so NotEmpty and [Required] read alike, in Hungarian too, and leaves every other
// key, and every language the catalog does not speak, to the manager that was there: FluentValidation's own
// translations. A manager the app sets itself replaces this one, and a rule's WithMessage never asks either.
internal sealed class CatalogLanguageManager(ILanguageManager previous) : ILanguageManager
{
    private static int _installed;

    public bool Enabled
    {
        get => previous.Enabled;
        set => previous.Enabled = value;
    }

    public CultureInfo Culture
    {
        get => previous.Culture;
        set => previous.Culture = value;
    }

    internal static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1)
        {
            return;
        }

        ValidatorOptions.Global.LanguageManager = new CatalogLanguageManager(ValidatorOptions.Global.LanguageManager);
    }

    // The language is the app's fixed one, else the reader's: Rask's, which in a live session is the visitor's rather
    // than the thread's. A manager switched off speaks FluentValidation's English, and so does this.
    public string GetString(string key, CultureInfo? culture = null)
    {
        if (!previous.Enabled)
        {
            return previous.GetString(key, culture);
        }

        var language = culture ?? previous.Culture ?? RaskCulture.CurrentUI;
        return CatalogSentences.For(key, language) ?? previous.GetString(key, language);
    }
}
