using System.Reflection;
using Rask.Core.Globalization;

namespace Rask.Wasm;

/// <summary>
///     The languages the app's <c>Rask:Cultures</c> names, as its build baked them into the app's assembly.
/// </summary>
/// <remarks>
///     A browser app has no configuration to bind, so the build reads the section instead — out of
///     <c>wwwroot/appsettings.json</c>, or the host's <c>appsettings.json</c> for a hosted client — and writes it
///     here as <c>[assembly: AssemblyMetadata("Rask.Cultures", "en,hu")]</c>. The same read decides whether the
///     bundle ships ICU, so the list the app runs with and the culture data it carries cannot disagree.
/// </remarks>
internal static class ConfiguredCultures
{
    internal const string MetadataKey = "Rask.Cultures";

    /// <summary>Adds the baked languages to <paramref name="options" />, in the order the file lists them.</summary>
    public static void AddTo(RaskCultureOptions options, Assembly? app)
    {
        var baked = app?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, MetadataKey, StringComparison.Ordinal))?.Value;

        foreach (var name in Parse(baked).Where(n => !options.SupportedCultures.Contains(n, StringComparer.OrdinalIgnoreCase)))
        {
            options.SupportedCultures.Add(name);
        }
    }

    internal static IEnumerable<string> Parse(string? baked) =>
        (baked ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
