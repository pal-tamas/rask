using Rask.Storage;

namespace Rask.Dashboard.Panels;

/// <summary>Files and bytes kept by one provider.</summary>
public sealed record ProviderUsage(StorageProvider Provider, int Files, long Bytes);
