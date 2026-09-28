namespace Rask.Core;

/// <summary>
///     Opt-out marker for the URL-attribute scheme sanitization Rask applies by default to
///     <c>href</c>/<c>src</c>/<c>action</c>/<c>cite</c>/etc. Wrap a value you fully control in
///     <see cref="Trusted" /> to emit it verbatim (it is still HTML-encoded). Use only for URLs
///     that are not attacker-influenced — e.g. a hard-coded <c>javascript:void(0)</c> sentinel.
/// </summary>
public static class RaskUrl
{
    // A sentinel prefix that the sanitizer recognizes and strips before output; a real URL would
    // never start with it. If it ever leaked unstripped it would be HTML-encoded harmlessly.
    internal const string TrustedPrefix = "rask-trusted:";

    /// <summary>
    ///     Marks <paramref name="url" /> as trusted so the next URL-attribute emit skips scheme
    ///     sanitization. The value is still HTML-encoded. Only use for non-attacker-controlled URLs.
    /// </summary>
    public static string Trusted(string url) => TrustedPrefix + (url ?? string.Empty);
}
