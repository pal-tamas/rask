namespace Rask.Mailing;

/// <summary>One email a test sent.</summary>
/// <param name="To">Its recipients' addresses.</param>
/// <param name="Subject">Its subject.</param>
/// <param name="Html">Its body, already rendered — <c>Body(component)</c> renders as the email is built.</param>
/// <param name="Delay">What <c>.In(…)</c> asked for, or <c>null</c>.</param>
/// <param name="Moment">What <c>.At(…)</c> asked for, or <c>null</c>.</param>
public sealed record SentEmail(
    IReadOnlyList<string> To,
    string Subject,
    string Html,
    TimeSpan? Delay,
    DateTimeOffset? Moment);
