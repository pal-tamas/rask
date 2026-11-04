using Microsoft.AspNetCore.Components.RenderTree;
using Rask.Core.Forms;

namespace Rask.Blazor;

/// <summary>
///     The disabled fieldsets a walk over a hosted component's render frames is inside (#1274), the same rule
///     Rask's own serializer keeps for its elements (<c>DisabledWalk</c>): a <c>&lt;fieldset disabled&gt;</c>
///     disables every control it holds, except inside its first <c>&lt;legend&gt;</c> child, which HTML leaves enabled.
/// </summary>
/// <remarks>
///     One per <see cref="BlazorFrameWriter.Write" />, carried down into nested components, whose frames are their
///     own buffers: a hosted child component drawn inside a disabled fieldset is drawn disabled too.
/// </remarks>
internal sealed class BlazorDisabledWalk(Func<ulong, string, BlazorValueKind, BlazorControl?, string?> register)
{
    private int _disabledFieldsets;
    private (RenderTreeFrame[]? Frames, int Index) _freeLegend;

    /// <summary>Turns a Blazor handler id into the Rask handler id to write; see <see cref="BlazorFrameWriter.Write" />.</summary>
    public Func<ulong, string, BlazorValueKind, BlazorControl?, string?> Register { get; } = register;

    /// <summary>Whether the walk is inside a disabled fieldset, outside its first legend.</summary>
    public bool InDisabledFieldset => _disabledFieldsets > 0;

    /// <summary>
    ///     Enters the children of the element at <paramref name="frames" />[<paramref name="element" />]: a disabled
    ///     fieldset's disable what they hold, its first legend's do not. Hands back what <see cref="Leave" /> restores.
    /// </summary>
    public Scope Enter(BlazorIslandRenderer renderer, ArrayRange<RenderTreeFrame> frames, int element, bool disabled)
    {
        ref var frame = ref frames.Array[element];
        if (disabled && DisabledControls.Is(frame.ElementName, DisabledControls.Fieldset))
        {
            var scope = Now();
            _disabledFieldsets++;
            _freeLegend = FirstLegend(renderer, frames, element + 1, element + frame.ElementSubtreeLength);
            return scope;
        }

        if (ReferenceEquals(_freeLegend.Frames, frames.Array) && _freeLegend.Index == element)
        {
            var scope = Now();
            _disabledFieldsets--;
            _freeLegend = default;
            return scope;
        }

        return default;
    }

    /// <summary>Leaves the children <see cref="Enter" /> entered.</summary>
    public void Leave(Scope scope)
    {
        if (scope.Entered)
        {
            _disabledFieldsets = scope.Depth;
            _freeLegend = scope.Legend;
        }
    }

    private Scope Now() => new(true, _disabledFieldsets, _freeLegend);

    // The fieldset's first <legend> child, as the browser will parse it: a region's frames and a nested component's
    // are the fieldset's own children once written, so both are looked through.
    private static (RenderTreeFrame[]? Frames, int Index) FirstLegend(
        BlazorIslandRenderer renderer, ArrayRange<RenderTreeFrame> frames, int start, int end)
    {
        var i = start;
        while (i < end)
        {
            ref var frame = ref frames.Array[i];
            switch (frame.FrameType)
            {
                case RenderTreeFrameType.Element when DisabledControls.Is(frame.ElementName, DisabledControls.Legend):
                    return (frames.Array, i);
                case RenderTreeFrameType.Element:
                    i += frame.ElementSubtreeLength;
                    break;
                case RenderTreeFrameType.Region:
                    if (FirstLegend(renderer, frames, i + 1, i + frame.RegionSubtreeLength) is { Frames: not null } inRegion)
                    {
                        return inRegion;
                    }

                    i += frame.RegionSubtreeLength;
                    break;
                case RenderTreeFrameType.Component:
                    var nested = renderer.FramesFor(frame.ComponentId);
                    if (FirstLegend(renderer, nested, 0, nested.Count) is { Frames: not null } inComponent)
                    {
                        return inComponent;
                    }

                    i += frame.ComponentSubtreeLength;
                    break;
                default:
                    i++;
                    break;
            }
        }

        return default;
    }

    /// <summary>What a fieldset or its legend changed of the walk, to put back as its children are left.</summary>
    internal readonly record struct Scope(bool Entered, int Depth, (RenderTreeFrame[]? Frames, int Index) Legend);
}
