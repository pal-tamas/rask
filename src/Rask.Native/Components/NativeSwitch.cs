using Rask.Native.Surface;

namespace Rask.Native.Components;

/// <summary>
///     An on/off toggle, projected to a <c>UISwitch</c> (iOS) or a <c>SwitchMaterial</c> (Android). Controlled:
///     it shows <see cref="On" /> and raises <see cref="OnChanged" />/<see cref="OnChangedAsync" /> with the
///     new state for you to store.
/// </summary>
/// <example>
///     <code>NativeSwitch(On: notify, OnChanged: v => { notify = v; })</code>
/// </example>
public sealed partial class NativeSwitch : NativeViewComponent
{
    /// <summary>Whether the switch is on. Leave <c>null</c> for off.</summary>
    public bool? On { get; set; }

    /// <summary>Whether the switch accepts interaction. Leave <c>null</c> for enabled.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Invoked with the new state when the user toggles it.</summary>
    public Action<bool>? OnChanged { get; set; }

    /// <summary>
    ///     The awaited form of <see cref="OnChanged" /> — use it when flipping the switch persists something.
    ///     Setting both runs the synchronous one first.
    /// </summary>
    public Func<bool, Task>? OnChangedAsync { get; set; }

    /// <summary>An accessibility identifier for screen readers and on-device E2E.</summary>
    public string? AccessibilityId { get; set; }

    /// <inheritdoc />
    internal override NativeNodeKind SurfaceKind => NativeNodeKind.Switch;

    /// <inheritdoc />
    internal override void WriteSurfaceProps(ref NativePropWriter props)
    {
        props.Flag(NativePropId.On, On);
        props.Flag(NativePropId.Enabled, Enabled);
        props.Handler(NativePropId.ChangeId, OnChanged ?? (Delegate?)OnChangedAsync, SurfaceChangeId);
        props.Text(NativePropId.AccessibilityId, AccessibilityId);
    }
}
