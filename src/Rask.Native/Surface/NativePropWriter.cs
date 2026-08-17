using Rask.Native.Components;

namespace Rask.Native.Surface;

/// <summary>
///     Collects a native component's props on the way to a <see cref="NativeNode" />. Every <c>Add</c> ignores a
///     <c>null</c>, so "the app did not set this" and "reset it to the platform default" are the same thing —
///     an absent prop — and a component's <c>WriteProps</c> stays a flat list of unconditional calls.
/// </summary>
internal struct NativePropWriter()
{
    private List<NativeProp>? _props;

    /// <summary>Adds a string prop, skipping a <c>null</c>.</summary>
    public void Text(NativePropId id, string? value)
    {
        if (value is not null)
        {
            (_props ??= []).Add(new NativeProp(id, NativePropValue.FromText(value)));
        }
    }

    /// <summary>Adds a numeric prop, skipping a <c>null</c>.</summary>
    public void Number(NativePropId id, double? value)
    {
        if (value.HasValue)
        {
            (_props ??= []).Add(new NativeProp(id, NativePropValue.FromNumber(value.Value)));
        }
    }

    /// <summary>Adds a boolean prop, skipping a <c>null</c>.</summary>
    public void Flag(NativePropId id, bool? value)
    {
        if (value.HasValue)
        {
            (_props ??= []).Add(new NativeProp(id, NativePropValue.FromFlag(value.Value)));
        }
    }

    /// <summary>Adds an enum prop as its integer value, skipping a <c>null</c>.</summary>
    public void Enum<TEnum>(NativePropId id, TEnum? value)
        where TEnum : struct, Enum =>
        Number(id, value.HasValue ? Convert.ToInt32(value.Value, System.Globalization.CultureInfo.InvariantCulture) : null);

    /// <summary>Adds a color prop as its <c>NativeColor</c> token, skipping a <c>null</c> or an unstyled color.</summary>
    public void Color(NativePropId id, NativeColor? value) => Text(id, value?.ToToken());

    /// <summary>
    ///     Adds a handler id, but only when the component actually supplied a delegate — the prop's ABSENCE is
    ///     what tells a backend not to make the view interactive at all.
    /// </summary>
    public void Handler(NativePropId id, Delegate? handler, int handlerId)
    {
        if (handler is not null)
        {
            Number(id, handlerId);
        }
    }

    /// <summary>
    ///     Materializes the props sorted by id, which is the invariant <c>NativeTreeDiffer</c> relies on to
    ///     compare two nodes with a single merge walk.
    /// </summary>
    public readonly NativeProp[] ToArray()
    {
        if (_props is null || _props.Count == 0)
        {
            return [];
        }

        var array = _props.ToArray();
        Array.Sort(array, static (a, b) => a.Id.CompareTo(b.Id));
        return array;
    }
}
