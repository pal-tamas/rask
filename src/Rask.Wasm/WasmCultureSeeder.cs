using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Rask.Core.Diagnostics;
using Rask.Core.Globalization;

namespace Rask.Wasm;

/// <summary>
///     Reads the browser's language signals and settles the app's culture before its first render.
/// </summary>
/// <remarks>
///     <para>
///         The WASM counterpart of the server's request negotiation, answering the same order:
///         <c>?culture=</c>, then the remembered cookie, then <c>navigator.languages</c>, then the app's
///         default. Because the whole app is one visitor, the culture it produces is a singleton rather
///         than something per session.
///     </para>
///     <para>
///         The payload is read with <see cref="JsonDocument" /> rather than deserialized into a record:
///         <c>Rask.Wasm</c> is marked trimmable and AOT-compatible under warnings-as-errors, so a
///         reflection-based <c>JsonSerializer.Deserialize</c> is a build error here. Three fields do not
///         justify a <c>JsonSerializerContext</c>.
///     </para>
/// </remarks>
internal static class WasmCultureSeeder
{
    /// <summary>
    ///     Negotiates from the browser and seeds the app's culture, or — when no languages are configured —
    ///     pins the document's own (see <see cref="PinWhenUnconfigured" />).
    /// </summary>
    public static void Seed(IServiceProvider services)
    {
        if (!IsConfigured(services, out var options))
        {
            PinDocumentLanguage();
            return;
        }

        var negotiation = Negotiate(JSInterop.GetCultureSignals(options.CookieName, options.QueryKey), options);

        if (services.GetService(typeof(IRaskCulture)) is SessionCulture culture)
        {
            culture.Seed(negotiation);
        }

        PinSessionCulture(services);
    }

    /// <summary>
    ///     Pins the process to the app's culture when it configured languages, so work outside a render or a
    ///     handler — a timer, a hosted service, a prerender's awaited load — formats as the page does.
    /// </summary>
    /// <remarks>The whole app is one visitor, so its culture is the process's.</remarks>
    public static void PinSessionCulture(IServiceProvider services)
    {
        if (IsConfigured(services, out _) && services.GetService(typeof(IRaskCulture)) is IRaskCulture { Supported.Count: > 0 } culture)
        {
            Pin(culture.Culture, culture.UICulture);
        }
    }

    /// <summary>
    ///     Pins the process to the document's language when the app configured none; otherwise leaves it,
    ///     because every render walk then pins the session's culture itself.
    /// </summary>
    /// <remarks>
    ///     dotnet-wasm starts in the BROWSER's locale, and a prerender in the BUILD MACHINE's. Neither is the
    ///     language of a page whose <c>&lt;html lang&gt;</c> is fixed at <c>"en"</c>, so left alone a Hungarian
    ///     browser titled an English page's calendar "2026. október", and the baked HTML depended on who ran
    ///     the publish. All four values, because a thread the runtime starts later reads the defaults.
    /// </remarks>
    public static void PinWhenUnconfigured(IServiceProvider services)
    {
        if (!IsConfigured(services, out _))
        {
            PinDocumentLanguage();
        }
    }

    private static bool IsConfigured(IServiceProvider services, [NotNullWhen(true)] out RaskCultureOptions? options)
    {
        options = services.GetService(typeof(RaskCultureOptions)) as RaskCultureOptions;
        return options is { SupportedCultures.Count: > 0 };
    }

    // The invariant culture when the runtime ships no culture data, which is what such an app formats in anyway.
    private static void PinDocumentLanguage()
    {
        _ = RaskCultureResolver.TryResolve(RaskCulture.UnconfiguredLanguage, out var culture);
        Pin(culture, culture);
    }

    private static void Pin(CultureInfo culture, CultureInfo uiCulture)
    {
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = uiCulture;
    }

    /// <summary>
    ///     Negotiates from a raw <c>getCultureSignals()</c> payload.
    /// </summary>
    /// <remarks>
    ///     Split from <see cref="Seed" /> so the decision can be asserted without a browser: on the
    ///     non-browser target framework the JS import is a stub that always answers <c>{}</c>, which
    ///     would make every test of this logic vacuous.
    /// </remarks>
    internal static CultureNegotiation Negotiate(string signalsJson, RaskCultureOptions options)
    {
        string? query = null;
        string? cookie = null;
        List<string>? languages = null;

        try
        {
            using var doc = JsonDocument.Parse(signalsJson);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                query = ReadString(root, "query");
                cookie = ReadString(root, "cookie");

                if (root.TryGetProperty("languages", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    languages = new List<string>(list.GetArrayLength());
                    foreach (var entry in list.EnumerateArray())
                    {
                        if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { Length: > 0 } tag)
                        {
                            languages.Add(tag);
                        }
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            // A malformed answer means a browser quirk, not a broken app: fall through to the default
            // culture rather than taking the boot down over a language preference.
            RaskDiagnostics.Report(
                RaskLogLevel.Warning,
                "Rask.Wasm",
                "[Rask.Wasm] could not read the browser's language signals; using the default culture",
                ex);
        }

        return RaskCultureNegotiator.Negotiate(query, cookie, languages, options);
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
