using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Rask.Spa.Hosting;

/// <summary>Source-generated metadata — the package is trim- and AOT-analysed under warnings-as-errors.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(StaticWebAssetsManifestFileProvider.Manifest))]
[SuppressMessage("Design", "CA1812", Justification = "Instantiated by the source-generated serializer.")]
internal sealed partial class ManifestJson : JsonSerializerContext;
