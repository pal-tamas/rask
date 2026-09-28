namespace Rask.WebPush;

/// <summary>One push a <see cref="PushFake" /> recorded.</summary>
/// <param name="Title">The notification's title.</param>
/// <param name="Body">Its body text.</param>
/// <param name="Url">Where a click opens.</param>
/// <param name="To">The user it was addressed to, or <see langword="null" /> for everyone.</param>
public sealed record SentPush(string? Title, string? Body, string? Url, Guid? To);
