namespace Rask.Dashboard.Panels;

/// <summary>How the database is configured, as far as the dashboard can see from the open connection.</summary>
/// <param name="Provider">The EF provider name.</param>
/// <param name="JournalMode">SQLite <c>journal_mode</c>, or <c>null</c> on another provider.</param>
/// <param name="ForeignKeys">SQLite <c>foreign_keys</c>, or <c>null</c> on another provider.</param>
/// <param name="SizeBytes">Database size in bytes, or <c>null</c> when the provider can't report it cheaply.</param>
public sealed record DatabaseInfo(string Provider, string? JournalMode, bool? ForeignKeys, long? SizeBytes);
