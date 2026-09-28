namespace Rask.Logging;

/// <summary>One key/value pair captured from an open logging scope.</summary>
/// <param name="Key">
/// The state key, e.g. <c>RequestId</c>. A scope opened with a bare object rather than key/value state
/// (<c>BeginScope("checkout")</c>) is stored under <see cref="LogScopeValue.MessageKey"/>.
/// </param>
/// <param name="Value">The value, already converted to a string at the call site.</param>
public readonly record struct LogScopeValue(string Key, string Value)
{
    /// <summary>The key a scope with no structured state is stored under.</summary>
    public const string MessageKey = "Scope";
}
