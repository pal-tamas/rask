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
    /// <summary>Negotiates from the browser and seeds the app's culture. A no-op when no languages are configured.</summary>
    public static void Seed(IServiceProvider services)
    {
        if (services.GetService(typeof(RaskCultureOptions)) is not RaskCultureOptions options
            || options.SupportedCultures.Count == 0)
        {
            return;
        }

        var negotiation = Negotiate(JSInterop.GetCultureSignals(), options);

        if (services.GetService(typeof(IRaskCulture)) is SessionCulture culture)
        {
            culture.Seed(negotiation);
        }
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
