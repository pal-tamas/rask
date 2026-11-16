using System.Globalization;
using Rask.Core.Diagnostics;
using Rask.Core.Globalization;

namespace Rask.Wasm;

/// <summary>
///     Puts an app's own resource assemblies (<c>Messages/ValidationMessages.hu.resx</c> →
///     <c>hu/App.resources.wasm</c>) in the runtime before anything reads them.
/// </summary>
/// <remarks>
///     <para>
///         dotnet.js downloads a satellite assembly only when asked for its culture, and a
///         <see cref="System.Resources.ResourceManager" /> that looks a culture up before it arrives falls back to the
///         neutral resources and REMEMBERS that — so the reader's culture is fetched before the first render, and
///         the app's other languages right after it, before the reader can switch to one.
///     </para>
///     <para>
///         A culture the app ships no resources for costs nothing: the runtime skips a culture its boot manifest
///         has no satellites of, without a request.
///     </para>
/// </remarks>
internal static class WasmSatellites
{
    private static readonly HashSet<string> _asked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The reader's culture, and the languages it falls back through: awaited before the first render.</summary>
    internal static Task LoadReaders(IServiceProvider services) =>
        Load(Chain(services.GetService(typeof(IRaskCulture)) is IRaskCulture culture ? culture.UICulture : CultureInfo.CurrentUICulture));

    /// <summary>Every other language the app supports, so switching to one finds its resources in place.</summary>
    internal static Task LoadOthers(IServiceProvider services) =>
        services.GetService(typeof(IRaskCulture)) is IRaskCulture { Supported.Count: > 0 } culture
            ? Load(culture.Supported.SelectMany(Chain))
            : Task.CompletedTask;

    /// <summary>Forgets what was asked: a test starts from a runtime with nothing loaded.</summary>
    internal static void Reset()
    {
        lock (_asked)
        {
            _asked.Clear();
        }
    }

    // A culture and its parents ("hu-HU", "hu"): ResourceManager looks for a satellite under each.
    internal static IEnumerable<string> Chain(CultureInfo culture)
    {
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            yield return c.Name;
        }
    }

    private static async Task Load(IEnumerable<string> cultures)
    {
        string[] fresh;
        lock (_asked)
        {
            // Asked once each: loading a satellite again would load its assembly twice.
            fresh = [.. cultures.Where(_asked.Add)];
        }

        if (fresh.Length == 0)
        {
            return;
        }

        try
        {
            await JSInterop.LoadSatellites(fresh).ConfigureAwait(false);
        }
        // A missing translation must not stop the app: it reads its neutral resources instead.
        catch (Exception ex)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Warning, "Rask.Wasm", $"The resources of {string.Join(", ", fresh)} could not be loaded", ex);
        }
    }
}
