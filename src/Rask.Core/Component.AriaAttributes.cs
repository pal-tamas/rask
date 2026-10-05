using System.Globalization;
using System.Numerics;
using System.Text;

namespace Rask.Core;

public abstract partial class Component
{
    internal partial class GlobalAttrs
    {
        // The typed aria-* values (Element.AriaLabel, AriaExpanded, …): one more reference on the side object, not on
        // LiveState, so an element that names none of them pays nothing, and one that names any pays only this.
        public AriaAttrs? Aria;
    }

    /// <summary>
    ///     The typed <c>aria-*</c> values an element names, kept as the text each renders as. Sparse: a bit per
    ///     attribute says which are set, and the values sit packed in slot order, so an element naming two of the
    ///     ~50 attributes holds two strings rather than fifty fields.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A boolean or keyword is stored as the interned literal it renders as (<c>"true"</c>, <c>"polite"</c>),
    ///         so setting and rendering one allocates nothing. The slots, their names and the key lookup are generated
    ///         from the snapshot's ARIA attributes (Rask.Dom.targets), in alphabetical order — the order they render in.
    ///     </para>
    ///     <para>
    ///         It also keeps the chain's reset for them. The typed properties are too many for a pending bit each, so
    ///         they share one (the chain generator's typed-ARIA group): every write here is recorded, and at the end
    ///         of the parent's render <see cref="ResetUnwritten" /> drops whatever no step wrote since the entry —
    ///         `Div.AriaExpanded(true)` on one render and a bare `Div` on the next renders no aria-expanded.
    ///     </para>
    /// </remarks>
    internal sealed partial class AriaAttrs
    {
        private ulong _set;
        private ulong _written;
        private string[] _values = new string[2];

        public string? Get(int slot)
        {
            var bit = 1UL << slot;
            return (_set & bit) == 0 ? null : _values[Rank(bit)];
        }

        public void Set(int slot, string? value)
        {
            var bit = 1UL << slot;
            _written |= bit;
            var rank = Rank(bit);
            var count = BitOperations.PopCount(_set);
            if ((_set & bit) != 0)
            {
                if (value is not null)
                {
                    _values[rank] = value;
                    return;
                }

                Array.Copy(_values, rank + 1, _values, rank, count - rank - 1);
                _values[count - 1] = null!;
                _set &= ~bit;
                return;
            }

            if (value is null)
            {
                return;
            }

            if (count == _values.Length)
            {
                Array.Resize(ref _values, count * 2);
            }

            Array.Copy(_values, rank, _values, rank + 1, count - rank);
            _values[rank] = value;
            _set |= bit;
        }

        // Drops every attribute no write named since the last reset, and starts the record again. True when one went.
        public bool ResetUnwritten()
        {
            var stale = _set & ~_written;
            for (var left = stale; left != 0; left &= left - 1)
            {
                Set(BitOperations.TrailingZeroCount(left), null);
            }

            _written = 0;
            return stale != 0;
        }

        // Whether a typed property already writes `aria-{key}`, so the Aria bag's entry for it is skipped.
        public bool Shadows(string key) => _set != 0 && SlotOf(key) is >= 0 and var slot && (_set & (1UL << slot)) != 0;

        public void Write(StringBuilder sb)
        {
            var set = _set;
            for (var i = 0; set != 0; i++, set &= set - 1)
            {
                AppendAttr(sb, Names[BitOperations.TrailingZeroCount(set)], _values[i]);
            }
        }

        private int Rank(ulong bit) => BitOperations.PopCount(_set & (bit - 1));

        // A number is formatted only when its text changed: a chain re-runs every render and sets the same value again.
        public void SetInteger(int slot, int? value) => SetFormatted(slot, value, format: null);

        public void SetNumber(int slot, double? value) => SetFormatted(slot, value, "R");

        private void SetFormatted<T>(int slot, T? value, string? format)
            where T : struct, ISpanFormattable
        {
            if (value is not { } v)
            {
                Set(slot, null);
                return;
            }

            Span<char> text = stackalloc char[32];
            if (Get(slot) is { } current && v.TryFormat(text, out var written, format, CultureInfo.InvariantCulture)
                && current.AsSpan().SequenceEqual(text[..written]))
            {
                _written |= 1UL << slot;
                return;
            }

            Set(slot, v.ToString(format, CultureInfo.InvariantCulture));
        }

        public static int? Integer(string? text) =>
            int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : null;

        public static double? Number(string? text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

        public static bool? Boolean(string? text) => text switch
        {
            "true" => true,
            "false" => false,
            _ => null,
        };

        public static string? Boolean(bool? value) => value switch
        {
            true => "true",
            false => "false",
            null => null,
        };
    }

    // For the generated Aria* properties: reading never allocates, and writing null to an element that never named an
    // ARIA attribute forces neither the LiveState nor a side object into existence.
    internal AriaAttrs? AriaAttrsInternal => _live?.Globals?.Aria;

    internal AriaAttrs? AriaAttrsForWrite(bool assigning) => assigning ? Globals.Aria ??= new AriaAttrs() : AriaAttrsInternal;

    // The typed-ARIA group's end-of-render reset (generated into BuilderRuntime.ResetElementPending): what the chain
    // stopped naming goes, and that is a prop change, as any other reset is.
    internal void ResetUnwrittenAria()
    {
        if (AriaAttrsInternal?.ResetUnwritten() == true)
        {
            MarkEntryPropsChangedInternal();
        }
    }
}
