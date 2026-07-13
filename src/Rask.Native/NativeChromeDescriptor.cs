using System.Text.Json.Serialization;

namespace Rask.Native;

// The wire shape the native heads read to build platform bars. Kept entirely inside Rask.Native (Core stays
// serialization-free) and serialized through the source-generated NativeChromeJsonContext so it is trim/AOT
// safe (iOS requires full AOT). Icons carry BOTH platform tokens (the head picks its own); buttons carry a tap
// id when they have an OnClick (the head echoes it back in a nativeTap event); tabs carry a route path.

internal sealed class NativeChromeDescriptor
{
    public NativeHeaderDescriptor? Header { get; set; }
    public NativeFooterDescriptor? Footer { get; set; }
}

internal sealed class NativeHeaderDescriptor
{
    public string? Title { get; set; }
    public NativeBarItemDescriptor? Leading { get; set; }
    public List<NativeBarItemDescriptor>? Trailing { get; set; }
}

internal sealed class NativeFooterDescriptor
{
    /// <summary><c>"tabbar"</c> (primary nav) or <c>"toolbar"</c> (contextual actions).</summary>
    public string Kind { get; set; } = "tabbar";

    public List<NativeTabDescriptor>? Tabs { get; set; }
    public int Selected { get; set; }
    public List<NativeBarItemDescriptor>? Items { get; set; }
}

internal sealed class NativeBarItemDescriptor
{
    /// <summary><c>"button"</c> or <c>"back"</c>.</summary>
    public string Kind { get; set; } = "button";

    /// <summary>The tap id echoed back in a <c>nativeTap</c> event; null for a display-only / back item.</summary>
    public string? Id { get; set; }

    public string? IosIcon { get; set; }
    public string? AndroidIcon { get; set; }
    public string? Title { get; set; }
}

internal sealed class NativeTabDescriptor
{
    public string? Title { get; set; }
    public string? IosIcon { get; set; }
    public string? AndroidIcon { get; set; }

    /// <summary>The route this tab navigates to (raised as a <c>navigate</c> event when tapped).</summary>
    public string Path { get; set; } = "/";
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NativeChromeDescriptor))]
internal sealed partial class NativeChromeJsonContext : JsonSerializerContext;
