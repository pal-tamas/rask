using Rask.Core.Live;

namespace Rask.Core.Forms;

// What a submit handler's field failures leave on the form (FieldFailurePlacement puts them here). A
// failure is held as ONE thing over every field it names, which is what lets a correction to any of them
// take it off all of them: a taken (year, number) pair stops being taken the moment either changes.
public sealed partial class EditContext
{
    private List<PlacedFailure>? _failures;

    // Latched by the first render that asks for the form's own messages: a summary, or an error for the
    // model itself. Never reset: a summary inside a cached component is not asked again on a later render.
    private bool _showsFormMessages;

    /// <summary>Whether a render has drawn the messages that belong to the form and to no field.</summary>
    internal bool ShowsFormMessages => _showsFormMessages;

    /// <summary>Whether a control on the form is bound to <paramref name="field" />.</summary>
    internal bool IsBound(FieldIdentifier field) =>
        _bindingOwners.ContainsKey(field) || _fieldDelegates.ContainsKey(field);

    /// <summary>
    ///     Whether <paramref name="field" /> holds a message, or is marked by a failure whose message is
    ///     under another field.
    /// </summary>
    internal bool IsInvalid(FieldIdentifier field)
    {
        MarkReader();
        if (_states.TryGetValue(field, out var state) && state.Messages.Count > 0)
        {
            return true;
        }

        // A loop, not a lambda: this is asked by every bound control on every render.
        if (_failures is { Count: > 0 } failures)
        {
#pragma warning disable S3267 // hot path: no enumerator/closure allocation
            foreach (var failure in failures)
#pragma warning restore S3267
            {
                if (Array.IndexOf(failure.Marked, field) >= 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    ///     Records one failure: its message under each of <paramref name="under" />, and
    ///     <paramref name="marked" /> invalid without one. Every field is touched, so the next keystroke
    ///     in one validates it again.
    /// </summary>
    internal void AddFailure(string message, FieldIdentifier[] under, FieldIdentifier[] marked)
    {
        (_failures ??= []).Add(new PlacedFailure(message, under, marked));
        foreach (var field in under)
        {
            AddValidationMessage(field, message);
            NotifyFieldTouched(field);
        }

        foreach (var field in marked)
        {
            NotifyFieldTouched(field);
        }

        ValidationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The model itself, with no field name: where a message about the whole form is kept.</summary>
    internal FieldIdentifier FormSlot => FormField;

    private void NoteFormMessagesRead()
    {
        if (!_showsFormMessages && LiveRenderContext.CurrentSync is not null)
        {
            _showsFormMessages = true;
        }
    }

    private void NoteMessagesRead(FieldIdentifier field)
    {
        if (!_showsFormMessages && field.FieldName.Length == 0 && ReferenceEquals(field.Model, Model))
        {
            NoteFormMessagesRead();
        }
    }

    // What validating one field starts with. Its rules are about to run again, and a failure a submit
    // handler threw is not one of them: that stays until the value changes or the form is submitted.
    private void ClearRuleMessages(FieldIdentifier field)
    {
        if (!_states.TryGetValue(field, out var state) || state.Messages.Count == 0)
        {
            return;
        }

        state.Messages.Clear();
        if (_failures is { Count: > 0 } failures)
        {
            state.Messages.AddRange(failures
                .Where(failure => Array.IndexOf(failure.Under, field) >= 0)
                .Select(failure => failure.Message));
        }

        ValidationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // Takes every failure that names `field` off ALL the fields it names.
    private void ClearFailuresNaming(FieldIdentifier field)
    {
        if (_failures is not { Count: > 0 } failures)
        {
            return;
        }

        var cleared = false;
        for (var i = failures.Count - 1; i >= 0; i--)
        {
            var failure = failures[i];
            if (Array.IndexOf(failure.Under, field) < 0 && Array.IndexOf(failure.Marked, field) < 0)
            {
                continue;
            }

            failures.RemoveAt(i);
            foreach (var under in failure.Under)
            {
                _states.GetValueOrDefault(under)?.Messages.Remove(failure.Message);
            }

            cleared = true;
        }

        if (cleared)
        {
            ValidationStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool ClearFailures()
    {
        if (_failures is not { Count: > 0 } failures)
        {
            return false;
        }

        failures.Clear();
        return true;
    }

    private sealed record PlacedFailure(string Message, FieldIdentifier[] Under, FieldIdentifier[] Marked);
}
