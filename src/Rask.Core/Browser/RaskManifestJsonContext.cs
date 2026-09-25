using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>Source-generated, trim-safe JSON metadata for <see cref="WebAppManifest" />.</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(WebAppManifest))]
internal sealed partial class RaskManifestJsonContext : JsonSerializerContext;
