namespace Rask.Storage;

/// <summary>Rows whose bytes are not on disk: how many, and the first few ids.</summary>
internal sealed record MissingFiles(int Count, IReadOnlyList<Guid> Examples);
