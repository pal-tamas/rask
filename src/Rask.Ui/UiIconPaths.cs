using System.Text;

namespace Rask;

// The tables themselves are UiIconPaths.g.cs, written by scripts/flux/icons.mjs. This half reads them.
internal static partial class UiIconPaths
{
    private const byte Separator = (byte)'|';

    private const int Variants = 4;

    // An icon's shapes, decoded the first time it is drawn and kept: a page redraws the same handful of
    // icons on every render. Unsynchronised on purpose — two threads decoding one icon write equal arrays,
    // and either is right.
    private static readonly UiIconShape[]?[] Decoded = new UiIconShape[]?[Variants * Count];

    /// <summary>The shapes of one icon in one variant.</summary>
    internal static UiIconShape[] For(Ui.IconName name, Ui.IconVariant variant)
    {
        var index = (int)name;
        if ((uint)index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(name), name, "Not a Heroicon: it has no path data.");
        }

        if ((uint)variant >= Variants)
        {
            throw new ArgumentOutOfRangeException(nameof(variant), variant, "Not an icon variant.");
        }

        return Decoded[((int)variant * Count) + index] ??= Decode(Encoded(variant, index));
    }

    private static ReadOnlySpan<byte> Encoded(Ui.IconVariant variant, int index) => variant switch
    {
        Ui.IconVariant.Solid => SolidData[SolidOffsets[index]..SolidOffsets[index + 1]],
        Ui.IconVariant.Mini => MiniData[MiniOffsets[index]..MiniOffsets[index + 1]],
        Ui.IconVariant.Micro => MicroData[MicroOffsets[index]..MicroOffsets[index + 1]],
        _ => OutlineData[OutlineOffsets[index]..OutlineOffsets[index + 1]],
    };

    private static UiIconShape[] Decode(ReadOnlySpan<byte> icon)
    {
        var shapes = new UiIconShape[icon.Count(Separator) + 1];
        for (var i = 0; i < shapes.Length; i++)
        {
            var end = icon.IndexOf(Separator);
            shapes[i] = Shape(end < 0 ? icon : icon[..end]);
            icon = icon[(end + 1)..];
        }

        return shapes;
    }

    private static UiIconShape Shape(ReadOnlySpan<byte> shape) => shape[0] switch
    {
        UiIconShape.EvenOdd or UiIconShape.RoundJoin or UiIconShape.Rectangle => new(shape[0], Encoding.ASCII.GetString(shape[1..])),
        _ => new(UiIconShape.Plain, Encoding.ASCII.GetString(shape)),
    };
}
