using Rask.Data;

namespace Rask.Background;

/// <summary>
///     What an <c>Enqueue(…).Noticed(…)</c> or a <c>Run(…).Noticed(…)</c> hands to the row it writes — its title, whether its
///     tenant shares it (<c>.ByTenant()</c>) and the batch it belongs to — for as long as that write is being made: ambient, as
///     the user and the tenant <see cref="Job.For" /> records are, so every <see cref="IJobs" /> (the real queue and the test
///     fake alike) records it without a parameter of its own.
/// </summary>
internal static class JobNotice
{
    /// <summary>The longest title a row keeps.</summary>
    public const int MaxTitle = 256;

    /// <summary>The longest report a row keeps; a longer one is cut there.</summary>
    public const int MaxProgress = 1024;

    /// <summary>The longest detail a row keeps (<c>Jobs.Warn</c>, <c>Jobs.Refuse</c>, <c>Jobs.Note</c>); a longer one is cut there.</summary>
    public const int MaxDetail = 1024;

    private static readonly AsyncLocal<Noticing?> Current = new();

    /// <summary>The title of the row being written now, or null.</summary>
    public static string? Pending => Current.Value?.Title;

    /// <summary>Whether the row being written now is shared with its tenant.</summary>
    public static bool PendingShared => Current.Value?.Shared ?? false;

    /// <summary>The batch the row being written now belongs to, or null.</summary>
    public static Guid? PendingBatch => Current.Value?.Batch;

    /// <summary>Makes <paramref name="title" /> the title of the rows written until disposed — null for none.</summary>
    public static Scope Use(string? title, bool shared = false, Guid? batch = null)
    {
        var previous = Current.Value;
        Current.Value = title is null && !shared && batch is null ? null : new Noticing(title, shared, batch);
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

    /// <summary>A detail a row can keep: cut at <see cref="MaxDetail" />, so a job never fails for saying too much.</summary>
    public static string CutDetail(string detail) => detail.Length > MaxDetail ? detail[..MaxDetail] : detail;

    /// <summary>
    ///     Refuses <c>.ByTenant()</c> where it would share nothing, in the sentence that fixes it: with no title there is no
    ///     notice to share, and with no tenant in flight nobody to share it with.
    /// </summary>
    /// <param name="call">The call being worded, as its author wrote it: <c>Jobs.Enqueue(job)</c>.</param>
    /// <param name="noticed">Whether it was worded <c>.Noticed(…)</c>.</param>
    public static void RefuseUnshareable(string call, bool noticed)
    {
        if (!noticed)
        {
            throw new InvalidOperationException(
                $"{call}.ByTenant() shares the job's notice with its tenant, and it has none: word .Noticed(\"…\") before "
                + ".ByTenant(), or drop .ByTenant().");
        }

        if (Tenant.Key is null)
        {
            throw new InvalidOperationException(
                $"{call}.Noticed(…).ByTenant() shares the job with its tenant, and no tenant is in flight here: enqueue it where "
                + "a tenant is (a signed-in request of a tenant, Tenant.Use(id)), or drop .ByTenant() to show it to its enqueuer "
                + "alone.");
        }
    }

    internal sealed record Noticing(string? Title, bool Shared, Guid? Batch);

    /// <summary>Puts the previous notice back.</summary>
    internal readonly struct Scope(Noticing? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
