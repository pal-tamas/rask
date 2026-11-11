namespace Rask;

/// <summary>
///     Bounds on what <c>Cached</c> keeps in memory when the app has no <c>HybridCache</c> of its own:
///     <c>services.Configure&lt;CachedOptions&gt;(o =&gt; o.SizeLimit = 128.Megabytes);</c>
/// </summary>
/// <remarks>
///     With a <c>HybridCache</c> registered the app's own cache decides what it holds, and only
///     <see cref="MaxEntrySize" /> and <see cref="DefaultLifetime" /> apply.
/// </remarks>
public sealed class CachedOptions
{
    /// <summary>The most markup, in bytes, held at once. Past it the least recently read goes first. 64 MB by default.</summary>
    public long SizeLimit { get; set; } = 64L * 1024 * 1024;

    /// <summary>A fragment drawing more than this many bytes is drawn live every time rather than stored. 1 MB by default.</summary>
    public long MaxEntrySize { get; set; } = 1024L * 1024;

    /// <summary>How long a fragment with no <c>.For(…)</c> of its own is kept. Five minutes by default.</summary>
    public TimeSpan DefaultLifetime { get; set; } = TimeSpan.FromMinutes(5);
}
