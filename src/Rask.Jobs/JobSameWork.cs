namespace Rask.Background;

/// <summary>
///     The key an <c>.UnlessWaiting(key)</c> names "the same work" by, handed to the row being written for as long as that
///     write is made — ambient, as <see cref="JobNotice" /> hands its title, so every <see cref="IJobs" /> (the real queue
///     and the test fake alike) sees it without a parameter of its own.
/// </summary>
internal static class JobSameWork
{
    /// <summary>The longest key a caller may name: it is hashed, so the length is for the caller's sake alone.</summary>
    public const int MaxKey = 1024;

    private static readonly AsyncLocal<string?> Current = new();

    /// <summary>The key of the row being written now, or null when its payload is what names its work.</summary>
    public static string? Pending => Current.Value;

    /// <summary>Makes <paramref name="key" /> the key of the rows written until disposed — null for the payload.</summary>
    public static Scope Use(string? key)
    {
        var previous = Current.Value;
        Current.Value = key;
        return new Scope(previous);
    }

    /// <summary>A key a caller may name: words, at most <see cref="MaxKey" /> characters.</summary>
    public static string Checked(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Length <= MaxKey
            ? key
            : throw new ArgumentOutOfRangeException(nameof(key), key.Length, $"A key of the same work is at most {MaxKey} characters.");
    }

    /// <summary>Puts the previous key back.</summary>
    internal readonly struct Scope(string? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
