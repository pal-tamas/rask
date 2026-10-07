namespace Rask;

// The default variant: the browser's own <select>, in the input's box.
public abstract partial class UiSelectControl<T>
{
    private Component Native(UiWithField field, Parts parts)
    {
        var look = UiClass.Compose(
            UiInputLook.Control,
            UiInputLook.Size(UiSelectLook.InputSize(Height)),
            UiInputLook.Outline,
            UiSelectLook.Native,
            Class);

        return NativeSelect(field, look, [.. NativeOptions(parts)]);
    }

    private IEnumerable<Component?> NativeOptions(Parts parts)
    {
        if (Placeholder is { } placeholder)
        {
            // Not pickable, and the chosen option until there is an answer: the select then shows it in grey.
            yield return Option.Value(string.Empty).Disabled().Selected(Current().Count == 0)[placeholder];
        }

        foreach (var block in parts.Blocks)
        {
            if (block.Group is { } group)
            {
                yield return Optgroup.Label(group.Label)[block.Rows.Select(NativeOption)];
                continue;
            }

            foreach (var row in block.Rows)
            {
                yield return NativeOption(row);
            }
        }
    }

    private static Component NativeOption(Row row) =>
        Option.Key(row.Option.Key ?? row.Option.FormValue).Value(row.Option.FormValue).Disabled(row.Option.Disabled == true)[
            row.Option.Text
        ];

    /// <summary>What the native control carries besides its classes: Flux's markers, and the invalid mark.</summary>
    /// <param name="invalid">Whether the field holds a message.</param>
    private protected static Dictionary<string, string?> NativeMarks(bool invalid)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["data-ui-control"] = null,
            ["data-ui-select-native"] = null,
            ["data-ui-group-target"] = null,
        };
        if (invalid)
        {
            marks["data-invalid"] = null;
        }

        return marks;
    }
}
