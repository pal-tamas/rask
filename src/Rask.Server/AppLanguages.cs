using Rask.Core.Globalization;

namespace Rask.Server;

// What naming an app's languages in code does to its culture options — the one shape both front doors share, so
// `AddRask(culture: "hu")` on a host wired by hand and `c.Cultures.Add("hu")` on a RaskApp cannot drift apart.
//
// Each language joins the list the configuration already made (Rask:Cultures), never replaces it, and the FIRST one
// named in code is the default: code wins over appsettings, so an appsettings list that starts with "en" does not
// quietly keep a Hungarian app's visitors in English.
internal static class AppLanguages
{
    internal static void Apply(RaskCultureOptions options, IReadOnlyList<string> languages)
    {
        if (languages.Count == 0)
        {
            return;
        }

        foreach (var language in languages.Where(language => !options.SupportedCultures.Contains(language, StringComparer.OrdinalIgnoreCase)))
        {
            options.SupportedCultures.Add(language);
        }

        options.DefaultCulture = languages[0];
    }
}
