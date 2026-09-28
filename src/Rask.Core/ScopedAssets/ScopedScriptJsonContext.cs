using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Core.ScopedAssets;

/// <summary>
///     Trim-safe metadata for what a generated scoped-script call puts on the wire by itself: the
///     primitives a TypeScript signature maps to, a callback's id and arguments, and <c>any</c>.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ScopedScript.ScriptCallback))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(double?))]
[JsonSerializable(typeof(bool?))]
[JsonSerializable(typeof(IReadOnlyList<double>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
internal sealed partial class ScopedScriptJsonContext : JsonSerializerContext;
