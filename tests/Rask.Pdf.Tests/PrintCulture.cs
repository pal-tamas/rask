using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Globalization;

namespace Rask.Printing.Tests;

/// <summary>
///     Every test here prints as in an app that ships English and Hungarian, and reads English unless it says otherwise.
/// </summary>
/// <remarks>
///     Naming an app's languages is process-wide with no way back, so it is on for every test from the start, as the
///     kit's own tests have it. The kit's Hungarian — the page numbers' among it — registers itself when the kit is
///     first touched, which is done here rather than left to whichever test draws a kit component first.
/// </remarks>
internal static class PrintCulture
{
    [ModuleInitializer]
    internal static void AnAppThatShipsEnglishAndHungarian()
    {
        new ServiceCollection().AddRaskCulture(options =>
        {
            options.SupportedCultures.Add("en");
            options.SupportedCultures.Add("hu");
        });
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        RuntimeHelpers.RunModuleConstructor(typeof(global::Rask.Ui).Module.ModuleHandle);
    }

    /// <summary>Runs <paramref name="work"/> for a caller who reads <paramref name="language"/>.</summary>
    /// <remarks>The language is set inside this method's own flow, so the caller's is untouched afterwards.</remarks>
    internal static async Task<T> In<T>(string language, Func<Task<T>> work)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        return await work();
    }

    internal static T In<T>(string language, Func<T> work)
    {
        var before = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        try
        {
            return work();
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }
}
