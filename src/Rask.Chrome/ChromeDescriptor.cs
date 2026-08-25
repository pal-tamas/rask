using System.Text.Json.Serialization;

namespace Rask.Chrome;

// The wire shape a native head reads to build platform bars.
//
// It lives in Rask.Chrome, not Rask.Native, because all four hosting models have to be able to PRODUCE one.
// In the in-process model the native session builds it; in the remote models the server or WASM session does,
// and ships it to the shell — those assemblies cannot see Rask.Native and should not. Rask.Chrome is the one
// assembly all three hosts already reference and already bundle, and it owns the portable bars this describes.
//
// Kept out of Rask.Core deliberately: Core names no chrome type and stays serialization-free. Serialized
// through the source-generated context below so it is trim/AOT safe (iOS requires full AOT). Icons carry BOTH
// platform tokens (the head picks its own); buttons carry a tap id when they have an OnClick (the head echoes
// it back as a nativeTap event); tabs carry a route path.

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

    // Optional appearance tokens (NativeColor.ToToken(): "#RRGGBBAA" or "light|dark"); null ⇒ platform default.
    public string? Background { get; set; }
    public string? Tint { get; set; }
    public string? TitleColor { get; set; }

    // Optional segmented control shown in place of the title; null ⇒ plain title.
    public List<NativeSegmentDescriptor>? Segments { get; set; }
    public int SelectedSegment { get; set; }
}

internal sealed class NativeSegmentDescriptor
{
    public string? Title { get; set; }

    /// <summary>The tap id echoed back (as a <c>nativeTap</c>) when this segment is selected; null if no handler.</summary>
    public string? Id { get; set; }
}

internal sealed class NativeFooterDescriptor
{
    /// <summary><c>"tabbar"</c> (primary nav) or <c>"toolbar"</c> (contextual actions).</summary>
    public string Kind { get; set; } = "tabbar";

    public List<NativeTabDescriptor>? Tabs { get; set; }
    public int Selected { get; set; }
    public List<NativeBarItemDescriptor>? Items { get; set; }

    // Optional appearance tokens; null ⇒ platform default. UnselectedTint applies to the tab-bar kind only.
    public string? Background { get; set; }
    public string? Tint { get; set; }
    public string? UnselectedTint { get; set; }
}

internal sealed class NativeBarItemDescriptor
{
    /// <summary><c>"button"</c>, <c>"back"</c>, or <c>"menu"</c> (an overflow pull-down).</summary>
    public string Kind { get; set; } = "button";

    /// <summary>The tap id echoed back in a <c>nativeTap</c> event; null for a display-only / back / menu item.</summary>
    public string? Id { get; set; }

    public string? IosIcon { get; set; }
    public string? AndroidIcon { get; set; }
    public string? Title { get; set; }

    /// <summary>The menu entries for a <c>"menu"</c> item; null otherwise.</summary>
    public List<NativeMenuItemDescriptor>? Menu { get; set; }
}

internal sealed class NativeMenuItemDescriptor
{
    public string? Title { get; set; }
    public string? IosIcon { get; set; }
    public string? AndroidIcon { get; set; }

    /// <summary>The tap id echoed back when this entry is selected; null for a display-only entry.</summary>
    public string? Id { get; set; }

    public bool Destructive { get; set; }
}

internal sealed class NativeTabDescriptor
{
    public string? Title { get; set; }
    public string? IosIcon { get; set; }
    public string? AndroidIcon { get; set; }

    /// <summary>The route this tab navigates to (raised as a <c>navigate</c> event when tapped).</summary>
    public string Path { get; set; } = "/";

    /// <summary>Optional badge text (e.g. an unread count); null/empty ⇒ no badge.</summary>
    public string? Badge { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NativeChromeDescriptor))]
internal sealed partial class NativeChromeJsonContext : JsonSerializerContext;
