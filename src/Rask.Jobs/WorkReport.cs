namespace Rask.Background;

/// <summary>What a job last reported of itself: its words, and how much of how much it has done — each null when it said none.</summary>
/// <param name="Progress">Its words (<c>Jobs.Report("…")</c>).</param>
/// <param name="Done">How much it has done.</param>
/// <param name="Of">How much there is to do.</param>
internal readonly record struct WorkReport(string? Progress, int? Done, int? Of);
