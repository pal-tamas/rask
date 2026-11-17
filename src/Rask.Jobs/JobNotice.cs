namespace Rask.Background;

/// <summary>
///     The title an <c>Enqueue(…).Noticed(…)</c> or a <c>Run(…).Noticed(…)</c> hands to the row it writes, for as long as that
///     write is being made — ambient, as the user and the tenant <see cref="Job.For" /> records are, so every
///     <see cref="IJobs" /> (the real queue and the test fake alike) records it without a parameter of its own.
/// </summary>
internal static class JobNotice
{
    /// <summary>The longest title a row keeps.</summary>
    public const int MaxTitle = 256;

    /// <summary>The longest report a row keeps; a longer one is cut there.</summary>
    public const int MaxProgress = 1024;

    private static readonly AsyncLocal<string?> Current = new();

    /// <summary>The title of the row being written now, or null.</summary>
    public static string? Pending => Current.Value;

    /// <summary>Makes <paramref name="title" /> the title of the rows written until disposed — null for none.</summary>
    public static Scope Use(string? title)
    {
        var previous = Current.Value;
        Current.Value = title;
        return new Scope(previous);
    }

    /// <summary>A title a row can keep: words, and no longer than <see cref="MaxTitle" />.</summary>
    public static string Checked(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var trimmed = title.Trim();
        return trimmed.Length <= MaxTitle
            ? trimmed
            : throw new ArgumentOutOfRangeException(
                nameof(title), trimmed.Length, $"A notice's title is at most {MaxTitle} characters — it is the line its enqueuer reads.");
    }

    /// <summary>A report a row can keep: cut at <see cref="MaxProgress" />, so a job never fails for saying too much.</summary>
    public static string? Cut(string? progress) =>
        progress is { Length: > MaxProgress } ? progress[..MaxProgress] : progress;

    /// <summary>Puts the previous title back.</summary>
    internal readonly struct Scope(string? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
