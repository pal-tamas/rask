using Rask.Core.Globalization;

namespace Rask.Core.Forms;

// The fields whose last value could not be read (UnreadValue). The model holds what it held before, so no rule
// has a value to judge: until a value arrives that can be read, the field says the one sentence about what
// was typed — ahead of "required" and of the app's own rule, whichever validators the form has — and no save
// goes through. The first keystroke of a correction takes the sentence away, as it does any message; the field
// stays unread until the correction itself arrives, which is before whatever the reader does next.
public sealed partial class EditContext
{
    private Dictionary<FieldIdentifier, RaskString>? _unread;

    /// <summary>Notes that what was typed into <paramref name="field" /> could not be read, and says so under it.</summary>
    internal void NoteUnread(FieldIdentifier field, RaskString sentence)
    {
        (_unread ??= [])[field] = sentence;
        var state = GetOrCreate(field);
        state.Modified = true;
        state.Touched = true;
        ClearMessages(field);
        AddValidationMessage(field, UnreadValue.Say(sentence));
    }

    /// <summary>A value arrived for <paramref name="field" /> that could be read.</summary>
    private void ForgetUnread(FieldIdentifier field) => _unread?.Remove(field);

    private bool IsUnread(FieldIdentifier field) => _unread is { Count: > 0 } unread && unread.ContainsKey(field);

    // What validating one field comes to while it is unread: its sentence, and no rule asked.
    private bool SayUnread(FieldIdentifier field)
    {
        if (_unread is not { Count: > 0 } unread || !unread.TryGetValue(field, out var sentence))
        {
            return false;
        }

        AddValidationMessage(field, UnreadValue.Say(sentence));
        return true;
    }

    // What validating the form starts with: every unread field says its sentence before any rule runs.
    private void SayUnread()
    {
        if (_unread is not { Count: > 0 } unread)
        {
            return;
        }

        foreach (var (field, sentence) in unread)
        {
            AddValidationMessage(field, UnreadValue.Say(sentence));
        }
    }
}
