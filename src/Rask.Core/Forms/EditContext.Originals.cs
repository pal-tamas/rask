using System.Collections;
using System.Runtime.InteropServices;

namespace Rask.Core.Forms;

// What each bound field held before the reader changed it, so the form can say what is unsaved and put it back.
//
// WHEN A VALUE IS ORIGINAL. A control bound to a field remembers the field's value the first time it is drawn, and
// the form remembers it afresh after every submit it accepts, and after Reset. A field the reader changed and then
// changed back holds its original again, and is not modified: "unsaved" is a difference, not a history.
//
// WHAT IS COMPARED. A value with Equals, and a collection (a pillbox's tags, a multiple select's picks) by what it
// holds, in order: the snapshot is a copy, since the bound collection is the same object before and after a pick.
//
// A PICK NOT WRITTEN YET. A control drawn in C# holds what the reader chose until the next action (PendingBinds),
// and the model has not heard it; the pick it holds is what is compared until the write arrives.
public sealed partial class EditContext
{
    // The component the form is: Reset draws it again, so its marks for the browser go with the values.
    internal Component? Form { get; set; }

    /// <summary>
    ///     How many times <see cref="Reset" /> or <see cref="MarkSaved" /> has run: the form writes it, and the browser
    ///     takes what each field shows then as its original.
    /// </summary>
    internal int Settled { get; private set; }

    /// <summary>
    ///     Whether any field the form's controls are bound to holds a value other than its original — the one
    ///     it held when the form was first drawn, when a submit was last accepted, or when the form was last
    ///     <see cref="Reset">reset</see>. A field changed and changed back again is not modified.
    /// </summary>
    /// <remarks>
    ///     The server's word, so it hears what the reader typed when the field sends it — with the next action,
    ///     or at a pause, or as it is typed (<c>.Blur()</c>, <c>.Debounce(…)</c>, <c>.Live()</c>). The page
    ///     marks a changed form in the browser at once: <c>data-dirty</c> on the <c>&lt;form&gt;</c> and on
    ///     each changed field.
    /// </remarks>
    public bool IsModifiedAny
    {
        get
        {
            MarkReader();
            foreach (var (bound, state) in _states)
            {
                if (Differs(bound, state))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    ///     Puts every bound field back to its original value — the one it held when the form was first drawn,
    ///     or when a submit was last accepted — and forgets that it was touched, modified or told anything:
    ///     a form ready for the next entry, as after "Save and add another".
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only the fields a control is bound to are put back; a property no control draws is the page's.
    ///         A collection is refilled in place when it is a list, and replaced by a copy when it is an array.
    ///     </para>
    ///     <para>
    ///         The form is drawn again with the values, and the page's <c>data-dirty</c> marks go with them.
    ///     </para>
    /// </remarks>
    public void Reset()
    {
        foreach (var binding in _bindingOwners.Values)
        {
            if (binding is { HasOriginal: true, Accessor: { } accessor })
            {
                FieldValues.Restore(accessor, binding.Original);
            }
        }

        foreach (var state in _states.Values)
        {
            state.Modified = false;
            state.Touched = false;
            state.HasPick = false;
            state.Pick = null;
            state.Cts?.Cancel();
        }

        _unread?.Clear();
        _typedInto = null;
        ClearAllMessages();

        // Everyone who draws a field draws it again with the value it holds now.
        foreach (var binding in _bindingOwners.Values)
        {
            binding.Owner?.StateHasChanged();
        }

        Settle();
    }

    /// <summary>
    ///     Says that what every bound field holds now is saved, with no submit: from here it is each field's original,
    ///     so the form is no longer modified and the page's <c>data-dirty</c> marks go. For a form that saves as the
    ///     reader goes — an <c>AfterBind</c> that writes the record — where an accepted submit would otherwise say it.
    /// </summary>
    /// <remarks>
    ///     Nothing is put back and no message is cleared; <see cref="Reset" /> is the one that starts the form again.
    /// </remarks>
    public void MarkSaved()
    {
        NoteSaved();
        Settle();
    }

    // The form is drawn again, and its count changes: the browser takes what each field shows as its original.
    private void Settle()
    {
        Settled++;
        Form?.StateHasChanged();
        RequestRender?.Invoke();
    }

    /// <summary>
    ///     Remembers how to read and write <paramref name="field" />, and its original value the first time it
    ///     is drawn. Called on every render of a bound control: after the first it stores one reference.
    /// </summary>
    internal void Remember(FieldIdentifier field, ExpressionAccessor.Accessor accessor)
    {
        ref var binding = ref CollectionsMarshal.GetValueRefOrAddDefault(_bindingOwners, field, out _);
        binding.Accessor = accessor;
        if (!binding.HasOriginal)
        {
            binding.Original = FieldValues.Snapshot(accessor.Getter());
            binding.HasOriginal = true;
        }
    }

    /// <summary>A submit was accepted: what every field holds now is what is saved.</summary>
    internal void NoteSaved()
    {
        foreach (var state in _states.Values)
        {
            state.Modified = false;
            state.HasPick = false;
            state.Pick = null;
        }

        foreach (var field in _bindingOwners.Keys)
        {
            ref var binding = ref CollectionsMarshal.GetValueRefOrNullRef(_bindingOwners, field);
            if (binding.Accessor is { } accessor)
            {
                binding.Original = FieldValues.Snapshot(accessor.Getter());
                binding.HasOriginal = true;
            }
        }
    }

    private bool Differs(FieldIdentifier field, FieldState state)
    {
        if (!state.Modified)
        {
            return false;
        }

        // A value the model could not take is not the original, whatever the model still holds.
        if (IsUnread(field) || !_bindingOwners.TryGetValue(field, out var binding)
            || binding is not { HasOriginal: true, Accessor: { } accessor })
        {
            return true;
        }

        var now = state.HasPick ? state.Pick : accessor.Getter();
        return !FieldValues.Same(binding.Original, now);
    }

    // One bound field: who drew it (re-rendered by a write), how to read and write it, and what it first held.
    private struct Binding
    {
        public Component? Owner;
        public ExpressionAccessor.Accessor? Accessor;
        public object? Original;
        public bool HasOriginal;
    }

    // Value comparison and copying for the originals: one responsibility, kept out of the context.
    private static class FieldValues
    {
        /// <summary>The value, or a copy of what a collection holds: the bound collection itself changes in place.</summary>
        internal static object? Snapshot(object? value) => value switch
        {
            null or string => value,
            Array array => array.Clone(),
            IEnumerable items => Copy(items),
            _ => value,
        };

        internal static bool Same(object? original, object? now)
        {
            if (original is IEnumerable before and not string && now is IEnumerable after and not string)
            {
                return SameItems(before, after);
            }

            return Equals(original, now);
        }

        internal static void Restore(ExpressionAccessor.Accessor accessor, object? original)
        {
            switch (original)
            {
                case Array array:
                    accessor.Setter(array.Clone());
                    return;
                case List<object?> items when accessor.Getter() is IList { IsReadOnly: false, IsFixedSize: false } list:
                    list.Clear();
                    foreach (var item in items)
                    {
                        list.Add(item);
                    }

                    return;
                case List<object?>:
                    // A collection that is neither a list nor an array cannot be refilled without knowing its type.
                    return;
                default:
                    accessor.Setter(original);
                    return;
            }
        }

        private static List<object?> Copy(IEnumerable items)
        {
            var copy = new List<object?>();
            foreach (var item in items)
            {
                copy.Add(item);
            }

            return copy;
        }

        private static bool SameItems(IEnumerable before, IEnumerable after)
        {
            var left = before.GetEnumerator();
            var right = after.GetEnumerator();
            try
            {
                while (true)
                {
                    var more = left.MoveNext();
                    if (more != right.MoveNext())
                    {
                        return false;
                    }

                    if (!more)
                    {
                        return true;
                    }

                    if (!Equals(left.Current, right.Current))
                    {
                        return false;
                    }
                }
            }
            finally
            {
                (left as IDisposable)?.Dispose();
                (right as IDisposable)?.Dispose();
            }
        }
    }
}
