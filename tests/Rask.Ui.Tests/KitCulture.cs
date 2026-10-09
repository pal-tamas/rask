using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Globalization;

namespace Rask.UiTests;

/// <summary>
///     Every test here runs as in an app that ships English and Hungarian, and reads English unless it says
///     otherwise.
/// </summary>
/// <remarks>
///     The kit's own Hungarian applies only once an app names its languages, and that switch is process-wide
///     with no public way back. Turned on by one test it would reach whichever tests ran after it, so it is on
///     for all of them from the start — which also holds every other test to what a localized app renders.
/// </remarks>
internal static class KitCulture
{
    [ModuleInitializer]
    internal static void AnAppThatShipsEnglishAndHungarian()
    {
        new ServiceCollection().AddRaskCulture(options =>
        {
            options.SupportedCultures.Add("en");
            options.SupportedCultures.Add("hu");
        });

        // The machine's language is not the test's business: a Hungarian desktop would otherwise fail every
        // assertion on an English label.
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    /// <summary>What <paramref name="render" /> gives a visitor who reads <paramref name="language" />.</summary>
    internal static string In(string language, Func<string> render)
    {
        var before = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        try
        {
            return render();
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }
}
