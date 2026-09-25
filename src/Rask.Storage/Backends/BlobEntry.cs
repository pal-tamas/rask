namespace Rask.Storage.Backends;

/// <summary>One object in a listing.</summary>
internal readonly record struct BlobEntry(string Key, long Size, DateTimeOffset LastModified);
