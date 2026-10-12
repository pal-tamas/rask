namespace Rask.Core.Forms;

// What the reader chose in controls that draw themselves in C# and has not been written to the model yet.
//
// A bound input keeps what was typed in the browser until the next action (rask-input.ts). A listbox, a calendar
// or a menu's radio cannot: the pick is a click the server hears, because the server draws the list. So the
// control's own event still arrives and the control is drawn again with the pick — but the WRITE to the model,
// its rules and AfterBind wait here, one list per session, and go in the order the controls were last touched
// before the next thing that is not such a control's own doing: a handler the page wrote, a submit, another
// control's change, a callback the control itself raises into the page.
//
// One static count gates every read: a session with nothing waiting pays one volatile read per event.
internal sealed class PendingBinds(Component root)
{
    private static int _waiting;

    private readonly List<Entry> _entries = [];
    private Task _settling = Task.CompletedTask;

    /// <summary>Whether any session holds a value that waits, or is still running the rules of one.</summary>
    internal static bool AnyWaiting => Volatile.Read(ref _waiting) != 0;

    /// <summary>The value a control chose and the session still holds for <paramref name="field" />.</summary>
    internal static bool TryPeek(Component control, FieldIdentifier field, out object? value)
    {
        value = null;
        return AnyWaiting && control.RenderHandle?.PendingBinds is { } binds && binds.Claim(control, field, ref value);
    }

    // A control is known by its place among its siblings, so the instance that drew a member last render may
    // draw another one now. The pick belongs to the member: whoever draws it holds it, and a pick of the member
    // this instance drew before has lost its control.
    private bool Claim(Component control, FieldIdentifier field, ref object? value)
    {
        var held = false;
        foreach (var entry in _entries)
        {
            if (entry.Field == field)
            {
                entry.Control = control;
                value = entry.Value;
                held = true;
            }
            else if (ReferenceEquals(entry.Control, control))
            {
                entry.Control = null;
            }
        }

        return held;
    }

    /// <summary>
    ///     Takes a value a control chose. Written at once when the control says <c>.Live()</c> or no session
    ///     is there to hold it, and with the next action otherwise.
    /// </summary>
    /// <param name="control">The control that chose.</param>
    /// <param name="field">The bound member.</param>
    /// <param name="context">The form the member belongs to.</param>
    /// <param name="value">What the control draws until the value is written.</param>
    /// <param name="write">Writes the model. False when the member cannot take the value.</param>
    /// <param name="settle">Runs the member's rules and <c>AfterBind</c>, after the write.</param>
    internal static Task Commit(
        Component control, FieldIdentifier field, EditContext? context, object? value, Func<bool> write, Func<Task> settle)
    {
        if (!Waits(control))
        {
            return WriteNow(control, write, settle);
        }

        if (control.RenderHandle?.PendingBinds is not { } binds)
        {
            return write() ? settle() : Task.CompletedTask;
        }

        binds.Hold(new Entry(control, field, value, write, settle));

        // The reader has edited the field, whatever the model says: the form is dirty, and a message about the
        // value being replaced goes, as it does under a field at the first key. The control is drawn again with
        // the pick. Nothing else is, because the model has not changed.
        context?.NoteEdited(field);
        control.StateHasChanged();
        return Task.CompletedTask;
    }

    // A control that says `.Live()` is an action like any other: what waited is written ahead of its value, so
    // its rules and its AfterBind read a model that has heard every pick.
    private static Task WriteNow(Component control, Func<bool> write, Func<Task> settle)
    {
        var binds = AnyWaiting ? control.RenderHandle?.PendingBinds : null;
        binds?.Write();
        if (!write())
        {
            return Task.CompletedTask;
        }

        return binds is null ? settle() : binds.Then(settle);
    }

    /// <summary>Whether a control's events are its own drawing rather than an action.</summary>
    internal static bool Waits(Component control) => control is IDeferredBind { Live: not true };

    /// <summary>Writes what waits before code a component of the page wrote runs, when a control raises it.</summary>
    internal static void Precede(Component author)
    {
        if (AnyWaiting)
        {
            PrecedeWaiting(author);
        }
    }

    private static void PrecedeWaiting(Component author)
    {
        if (author.RenderHandle?.PendingBinds is { } binds && !binds.IsOwn(author))
        {
            binds.Write();
        }
    }

    /// <summary>Whether a handler <paramref name="owner" /> wrote belongs to a control that waits.</summary>
    internal bool IsOwn(Component owner) =>
        _entries.Count == 0 || Waits(owner) || IsUnderWaiting(root, owner, false) is true;

    /// <summary>Writes everything that waits and runs its rules.</summary>
    internal async ValueTask<bool> FlushAsync()
    {
        Write();
        await _settling.ConfigureAwait(false);
        return true;
    }

    /// <summary>The rules of what a callback wrote ahead of itself, to be awaited once the handler is done.</summary>
    internal async ValueTask<bool> SettledAsync()
    {
        await _settling.ConfigureAwait(false);
        return true;
    }

    private Task Then(Func<Task> settle)
    {
        Interlocked.Increment(ref _waiting);
        return _settling = SettleOneAsync(_settling, settle);
    }

    private static async Task SettleOneAsync(Task earlier, Func<Task> settle)
    {
        try
        {
            await earlier.ConfigureAwait(false);
            await settle().ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
        }
    }

    // Every value first, then every rule: a rule or an AfterBind reading a second member finds it written.
    private void Write()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        var waiting = _entries.ToArray();
        _entries.Clear();

        // A control a render removed takes its pick with it. One removed by the action itself is still here.
        var written = waiting.Where(static entry => entry.Control is { IsTornDown: false } && entry.Write()).ToList();

        _settling = SettleAsync(_settling, written, waiting.Length);
    }

    private static async Task SettleAsync(Task earlier, List<Entry> written, int held)
    {
        try
        {
            await earlier.ConfigureAwait(false);
            foreach (var entry in written)
            {
                await entry.Settle().ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Add(ref _waiting, -held);
        }
    }

    /// <summary>Forgets what waits: the session is over.</summary>
    internal void Drop()
    {
        Interlocked.Add(ref _waiting, -_entries.Count);
        _entries.Clear();
    }

    // Last touched, last written: a field chosen again moves to the end, as a control does in the browser.
    private void Hold(Entry entry)
    {
        var at = _entries.FindIndex(held => held.Field == entry.Field);
        if (at >= 0)
        {
            _entries.RemoveAt(at);
        }
        else
        {
            Interlocked.Increment(ref _waiting);
        }

        _entries.Add(entry);
    }

    // Null when the owner is not in the tree: a handler of a component that has since gone.
    private static bool? IsUnderWaiting(Component node, Component owner, bool under)
    {
        if (ReferenceEquals(node, owner))
        {
            return under;
        }

        var below = under || Waits(node);
        foreach (var child in node.PersistedChildren.Values)
        {
            if (IsUnderWaiting(child, owner, below) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private sealed class Entry(Component control, FieldIdentifier member, object? value, Func<bool> write, Func<Task> settle)
    {
        // Null once a render gave the control another member to draw.
        public Component? Control { get; set; } = control;

        public FieldIdentifier Field { get; } = member;

        public object? Value { get; } = value;

        public Func<bool> Write { get; } = write;

        public Func<Task> Settle { get; } = settle;
    }
}
