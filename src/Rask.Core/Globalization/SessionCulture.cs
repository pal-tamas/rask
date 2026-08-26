using System.Globalization;
using Rask.Core.Diagnostics;

namespace Rask.Core.Globalization;

/// <summary>
///     The default <see cref="IRaskCulture" />: one visitor's culture, held for the life of their
///     session.
/// </summary>
/// <remarks>
///     Scoped per session on the server (one live session, one instance) and a singleton on WASM, where
///     the whole app <em>is</em> one session. The session reads this at the top of every render walk
///     rather than relying on the value propagating — see <see cref="RaskCultureScope" /> for why
///     propagation cannot be trusted.
/// </remarks>
public sealed class SessionCulture : IRaskCulture
{
    private readonly RaskCultureOptions _options;
    private readonly IRaskCulturePersistence? _persistence;
    private readonly IReadOnlyList<CultureInfo> _supported;

    /// <summary>Creates a session culture from the app's configured languages.</summary>
    public SessionCulture(RaskCultureOptions options, IRaskCulturePersistence? persistence = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _persistence = persistence;

        var supported = new List<CultureInfo>(options.SupportedCultures.Count);
        foreach (var name in options.SupportedCultures)
        {
            if (RaskCultureResolver.TryResolve(name, out var culture))
            {
                supported.Add(culture);
            }
        }

        _supported = supported;

        // One report, once per process, rather than one per failed lookup: an app that asked for
        // languages this runtime cannot produce has a build-configuration problem, and the actionable
        // part is the MSBuild property, not the individual culture.
        if (supported.Count < options.SupportedCultures.Count && !RaskCultureResolver.IsGlobalizationSupported)
        {
            RaskDiagnostics.ReportOnce(
                "rask.globalization:no-culture-data",
                RaskLogLevel.Warning,
                "Rask.Globalization",
                static () =>
                    "This app configures cultures, but the runtime was built without culture data, so "
                    + "every culture formats identically and only the invariant culture resolves. Set "
                    + "<RaskGlobalization>true</RaskGlobalization> in the app's project file to ship ICU "
                    + "(roughly 2.6 MB in a WASM bundle), or configure a single culture.");
        }

        var initial = RaskCultureNegotiator.Negotiate(null, null, null, options);
        Culture = initial.Culture;
        UICulture = initial.UICulture;
    }

    /// <inheritdoc />
    public CultureInfo Culture { get; private set; }

    /// <inheritdoc />
    public CultureInfo UICulture { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<CultureInfo> Supported => _supported;

    /// <inheritdoc />
    public bool IsRightToLeft => Culture.TextInfo.IsRightToLeft;

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public Task<bool> SetAsync(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return SetCoreAsync(culture.Name);
    }

    /// <inheritdoc />
    public Task<bool> SetAsync(string name) => SetCoreAsync(name);

    /// <summary>
    ///     Seeds the culture from a host's negotiation, before the first render. Does not persist and
    ///     does not raise <see cref="Changed" /> — nothing has rendered yet, so there is nothing to
    ///     invalidate, and the visitor has not chosen anything worth remembering.
    /// </summary>
    internal void Seed(CultureNegotiation negotiation)
    {
        Culture = negotiation.Culture;
        UICulture = negotiation.UICulture;
    }

    private async Task<bool> SetCoreAsync(string? name)
    {
        // TrySelect, not Negotiate: a switch honours the same matching rules ("hu" may select a
        // supported "hu-HU") but must not be gated on UseQueryString, which governs how a choice
        // travels rather than whether it counts.
        if (!RaskCultureNegotiator.TrySelect(name, _options, out var selected))
        {
            return false;
        }

        if (selected.Culture.Equals(Culture) && selected.UICulture.Equals(UICulture))
        {
            return false;
        }

        Culture = selected.Culture;
        UICulture = selected.UICulture;

        if (_options.UseCookie && _persistence is not null)
        {
            await _persistence.SaveAsync(Culture.Name, UICulture.Name).ConfigureAwait(false);
        }

        // The session subscribes to this and re-renders. Raised after persistence so a reload during
        // the render cannot observe the old preference.
        Changed?.Invoke();
        return true;
    }
}
