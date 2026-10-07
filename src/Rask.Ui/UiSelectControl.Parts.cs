namespace Rask;

// What the select was handed as children: its options, in the order written, and the parts Flux takes as slots.
public abstract partial class UiSelectControl<T>
{
    /// <summary>One option, typed: where it sits among all of them, and the value picking it stores.</summary>
    private sealed record Row(int Index, T Value, UiSelectOption Option);

    /// <summary>A run of the list as it is drawn: a group and its rows, loose rows, or a child that is neither.</summary>
    private sealed record Block(UiSelectGroup? Group, IReadOnlyList<Row> Rows, Component? Other);

    private sealed class Parts
    {
        private readonly List<Row> _rows = [];
        private readonly List<Block> _blocks = [];

        /// <summary>Every option, groups flattened, in the order written.</summary>
        internal List<Row> Rows => _rows;

        /// <summary>The list as it is drawn.</summary>
        internal List<Block> Blocks => _blocks;

        internal UiSelectOptionCreate? Create { get; private set; }

        /// <summary>Whether the create row was written before every option.</summary>
        internal bool CreateLeads { get; private set; }

        internal UiSelectOptionEmpty? Empty { get; private set; }

        internal UiSelectButton? Button { get; private set; }

        internal UiSelectInput? Input { get; private set; }

        internal UiSelectSearch? Search { get; private set; }

        internal static Parts Read(IEnumerable<Component?>? children)
        {
            var parts = new Parts();
            var loose = new List<Row>();
            foreach (var child in children ?? [])
            {
                if (child is UiSelectOption option)
                {
                    loose.Add(parts.Add(option));
                    continue;
                }

                if (child is null || parts.TakeSlot(child))
                {
                    continue;
                }

                parts.Flush(loose);
                parts._blocks.Add(child is UiSelectGroup group
                    ? new Block(group, [.. (group.Children ?? []).OfType<UiSelectOption>().Select(parts.Add)], null)
                    : new Block(null, [], child));
            }

            parts.Flush(loose);

            return parts;
        }

        private Row Add(UiSelectOption option)
        {
            var row = new Row(_rows.Count, ValueOf(option), option);
            _rows.Add(row);

            return row;
        }

        private void Flush(List<Row> loose)
        {
            if (loose.Count > 0)
            {
                _blocks.Add(new Block(null, [.. loose], null));
                loose.Clear();
            }
        }

        private bool TakeSlot(Component child)
        {
            switch (child)
            {
                case UiSelectOptionCreate create:
                    Create = create;
                    CreateLeads = _rows.Count == 0;
                    return true;
                case UiSelectOptionEmpty empty:
                    Empty = empty;
                    return true;
                case UiSelectButton button:
                    Button = button;
                    return true;
                case UiSelectInput input:
                    Input = input;
                    return true;
                case UiSelectSearch search:
                    Search = search;
                    return true;
                default:
                    return false;
            }
        }

        // An option cannot know which select it will be a child of, so its value is held to the select's type here.
        private static T ValueOf(UiSelectOption option) => option.Value switch
        {
            T typed => typed,
            null when (object)option.Text is T words => words,
            // No value, and words cannot stand in for one: the option that means "none" in a select that may hold none.
            null when default(T) is null => default!,
            _ => throw new InvalidOperationException(
                $"Ui.SelectOption \"{option.Text}\" holds {option.Value?.GetType().Name ?? "no value"}, and its Ui.Select holds "
                + $"{typeof(T).Name}: give it .Value(…) of that type."),
        };
    }
}
