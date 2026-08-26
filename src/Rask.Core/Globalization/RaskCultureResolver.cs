using System.Globalization;

namespace Rask.Core.Globalization;

/// <summary>
///     Turns a culture name into a <see cref="CultureInfo" /> without ever throwing, and reports whether
///     this runtime has real culture data at all.
/// </summary>
/// <remarks>
///     Every culture lookup in Rask goes through here, because the obvious call is a trap on the host we
///     care most about. A WASM app published with <c>InvariantGlobalization=true</c> — still the Rask
///     default, since ICU is ~2.6 MB — also gets <c>PredefinedCulturesOnly=true</c>, and there
///     <c>new CultureInfo("hu-HU")</c> does not fall back to invariant: it <b>throws</b>
///     <see cref="CultureNotFoundException" />. A framework that let that escape would turn "this app
///     supports Hungarian" into a blank page rather than into English text.
///     <para>
///         So the contract is: an unavailable culture is a <c>false</c>, never an exception, and the
///         caller carries on with what it already had. An app that asked for cultures it cannot have is
///         told once, at startup, by <see cref="SessionCulture" /> — not once per render.
///     </para>
/// </remarks>
public static class RaskCultureResolver
{
    private static readonly Lazy<bool> _supported = new(Probe, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    ///     Whether this runtime carries culture data. <c>false</c> under invariant globalization, where
    ///     every culture formats identically and only the invariant culture can be constructed.
    /// </summary>
    /// <remarks>
    ///     Probed by asking for a culture that certainly exists wherever ICU does, rather than read off an
    ///     <c>AppContext</c> switch: the switch is only one of the ways a runtime ends up invariant (the
    ///     MSBuild property, <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT</c>, and a runtimeconfig entry all
    ///     reach the same place), and what actually matters is whether a lookup works.
    /// </remarks>
    public static bool IsGlobalizationSupported => _supported.Value;

    /// <summary>
    ///     Resolves <paramref name="name" /> to a culture, returning <c>false</c> instead of throwing
    ///     when it is null, blank, malformed, or unknown to this runtime.
    /// </summary>
    /// <remarks>
    ///     A <c>true</c> here does not mean "this is a real language". .NET does not reject an
    ///     unknown-but-well-formed tag — ICU manufactures a culture for it — so <c>zz-ZZ-not-real</c>
    ///     resolves. That is why nothing in Rask treats a resolved name as trusted: negotiation matches
    ///     only against the list the app itself configured, so an invented tag arriving in a query
    ///     string or a cookie can never select anything.
    /// </remarks>
    public static bool TryResolve(string? name, out CultureInfo culture)
    {
        culture = CultureInfo.InvariantCulture;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            // GetCultureInfo rather than the constructor, so repeated lookups hit the BCL's own cache.
            var resolved = CultureInfo.GetCultureInfo(name.Trim());

            // Under a runtime with no culture data a lookup can still succeed and hand back something
            // invariant wearing the requested name, which would let "hu-HU" pose as real support.
            if (!IsGlobalizationSupported && !resolved.Equals(CultureInfo.InvariantCulture))
            {
                return false;
            }

            culture = resolved;
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // Malformed rather than merely unknown (embedded separators, over-long subtags).
            return false;
        }
    }

    private static bool Probe()
    {
        try
        {
            // Ask whether the runtime has culture DATA, not whether a lookup succeeds. Those are
            // different questions, and getting it wrong is silent: with PredefinedCulturesOnly turned
            // off — which is exactly what shipping ICU sets — GetCultureInfo never throws and hands
            // back a culture object that is not the invariant one, even when no ICU data was linked
            // in. An identity check therefore answers "yes" for a runtime that formats everything
            // identically, and an app is told it has languages it does not have.
            //
            // German writes 14.03.2026 where the invariant culture writes 03/14/2026. If those come
            // back the same, there is no data behind the name.
            var probe = new DateTime(2026, 3, 14);
            return !string.Equals(
                probe.ToString("d", CultureInfo.GetCultureInfo("de-DE")),
                probe.ToString("d", CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }
        catch (CultureNotFoundException)
        {
            // Invariant globalization with PredefinedCulturesOnly on: the lookup itself is refused.
            return false;
        }
    }
}
