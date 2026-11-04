using Rask.Core.Globalization;

namespace Rask.Core.Forms;

// The fields whose last value could not be read (UnreadValue). The model holds what it held before, so no rule
// has a value to judge: until a value arrives that can be read, the field says the one sentence about what
// was typed — ahead of "required" and of the app's own rule, whichever validators the form has — and no save
// goes through. The first keystroke of a correction takes the sentence away, as it does any message; the field
// stays unread until the correction itself arrives, which is before whatever the reader does next. A value read and
// still not taken — a date typed outside a picker's limits (#1273) — is held the same way, with a sentence of its own.
public sealed partial class EditContext
{
    private Dictionary<FieldIdentifier, string>? _unread;

    /// <summary>Notes that what was typed into <paramref name="field" /> could not be read, and says so under it.</summary>
    internal void NoteUnread(FieldIdentifier field, RaskString sentence) => NoteUnread(field, UnreadValue.Say(sentence));

    /// <summary>
    ///     Notes that what was typed into <paramref name="field" /> was not taken, and says <paramref name="said" />
    ///     under it — already in the reader's language — until a value that is taken arrives.
    /// </summary>
    internal void NoteUnread(FieldIdentifier field, string said)
    {
        (_unread ??= [])[field] = said;
        var state = GetOrCreate(field);
        state.Modified = true;
        state.Touched = true;
        ClearMessages(field);
        AddValidationMessage(field, said);
    }

    /// <summary>A value arrived for <paramref name="field" /> that could be read.</summary>
    private void ForgetUnread(FieldIdentifier field) => _unread?.Remove(field);

    internal bool IsUnread(FieldIdentifier field) => _unread is { Count: > 0 } unread && unread.ContainsKey(field);

    // What validating one field comes to while it is unread: its sentence, and no rule asked.
    private bool SayUnread(FieldIdentifier field)
    {
        if (_unread is not { Count: > 0 } unread || !unread.TryGetValue(field, out var said))
        {
            return false;
        }

        AddValidationMessage(field, said);
        return true;
    }

    // What validating the form starts with: every unread field says its sentence before any rule runs.
    private void SayUnread()
    {
        if (_unread is not { Count: > 0 } unread)
        {
            return;
        }

        foreach (var (field, said) in unread)
        {
            AddValidationMessage(field, said);
        }
    }
}
