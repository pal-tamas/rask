namespace Rask.Data;

/// <summary>Setters for the audit stamps the writer applies in the interceptor's place.</summary>
internal sealed record BulkTimestamps(Action<object, DateTime> SetCreatedAt, Action<object, DateTime> SetUpdatedAt);
