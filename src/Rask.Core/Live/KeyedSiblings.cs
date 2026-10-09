using System.Runtime.InteropServices;

namespace Rask.Core.Live;

/// <summary>
///     What one parent keeps about ONE child type it has keyed, beside the keyed children themselves: the
///     children of that type written WITHOUT a key, and the keys written more than once in a render.
/// </summary>
/// <remarks>
///     <para>
///         An entry cannot know whether a <c>Key</c> step will follow it, so it hands out the instance the
///         next UNKEYED child of the type held last render (<see cref="NextHeld" />), and the Key step, if
///         one comes, gives that back (<see cref="GiveBack" />) and takes the instance its key owns. An
///         unkeyed child is therefore identified by its order among the unkeyed children of its type,
///         which the keyed ones coming, going and moving around it cannot change (#1215).
///     </para>
///     <para>
///         The cost of handing a held instance to an entry that turns out to be keyed falls on a chain
///         that writes steps BEFORE its Key: they land on the unkeyed sibling's instance first. Its own
///         chain re-specifies it in full later in the same render, so what it shows is right, but it is
///         told its props changed. Key first costs nothing.
///     </para>
///     <para>
///         Allocated once per parent and type, by the first <c>Key</c> step. A render allocates nothing here.
///     </para>
/// </remarks>
internal sealed class KeyedSiblings
{
    // In the order written. `Held` marks an instance that ended an earlier render as an unkeyed child, as
    // opposed to one built during this render that a Key step may still claim or set aside.
    private readonly List<(Component? Instance, bool Held)> _unkeyed = [];
    private int _written;

    // The second and later uses of one key in a render, in the order written. Null for the parent whose
    // keys are unique, which is every parent written as the docs ask.
    private List<(object Key, Component Instance)>? _repeats;
    private List<(object Key, Component Instance)>? _previousRepeats;

    /// <summary>
    ///     An instance an entry built by position and its Key step then set aside for the one it kept. It
    ///     never started a lifecycle, so the next entry takes it instead of constructing another.
    /// </summary>
    public Component? Spare { get; set; }

    /// <summary>Whether a repeated key has been reported for this parent and type — once is enough.</summary>
    public bool RepeatReported { get; set; }

    /// <summary>The instance the next unkeyed child held last render, or null when there is none.</summary>
    public Component? NextHeld()
    {
        if (_written < _unkeyed.Count && _unkeyed[_written] is { Held: true, Instance: { } held })
        {
            _written++;
            return held;
        }

        return null;
    }

    /// <summary>Files <paramref name="built" />, an instance built this render, as the next unkeyed child.</summary>
    public void Hold(Component built)
    {
        if (_written < _unkeyed.Count)
        {
            _unkeyed[_written] = (built, false);
        }
        else
        {
            _unkeyed.Add((built, false));
        }

        _written++;
    }

    /// <summary>Offers <paramref name="held" />, alive since an earlier render, to an unkeyed child still to be written.</summary>
    public void Offer(Component held) => _unkeyed.Add((held, true));

    /// <summary>
    ///     Takes <paramref name="provisional" /> back out of the unkeyed children: a Key step followed its
    ///     entry after all. True when it is an instance an unkeyed child has held since an earlier render,
    ///     which a key must therefore not be given.
    /// </summary>
    public bool GiveBack(Component provisional)
    {
        // Backwards: the Key step runs straight after its entry in every chain, so the match is the last one.
        var unkeyed = CollectionsMarshal.AsSpan(_unkeyed);
        for (var i = _written - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(unkeyed[i].Instance, provisional))
            {
                continue;
            }

            var held = unkeyed[i].Held;
            var last = i == _written - 1;
            if (!last)
            {
                // A child written since has already moved past it: a gap, which nothing may fill with an
                // instance that is no longer an unkeyed child.
                unkeyed[i] = default;
                return held;
            }

            // A held instance stays where it is, for the next unkeyed child to take.
            _written--;
            if (!held)
            {
                _unkeyed.RemoveAt(i);
            }

            return held;
        }

        return false;
    }

    /// <summary>The instance the use of <paramref name="key" /> now being written had last render, if any.</summary>
    public Component? PreviousRepeat(object key)
    {
        var occurrence = 0;
        foreach (var (repeated, _) in CollectionsMarshal.AsSpan(_repeats))
        {
            if (repeated.Equals(key))
            {
                occurrence++;
            }
        }

        foreach (var (repeated, instance) in CollectionsMarshal.AsSpan(_previousRepeats))
        {
            if (repeated.Equals(key) && occurrence-- == 0)
            {
                return instance;
            }
        }

        return null;
    }

    public void FileRepeat(object key, Component instance) => (_repeats ??= []).Add((key, instance));

    /// <summary>Starts the next render: what was written becomes what is held, and the rest is let go.</summary>
    public void Rotate()
    {
        _unkeyed.RemoveRange(_written, _unkeyed.Count - _written);
        var unkeyed = CollectionsMarshal.AsSpan(_unkeyed);
        for (var i = 0; i < unkeyed.Length; i++)
        {
            unkeyed[i].Held = unkeyed[i].Instance is not null;
        }

        _written = 0;
        if (_repeats is not null)
        {
            (_previousRepeats, _repeats) = (_repeats, _previousRepeats ?? []);
            _repeats.Clear();
        }
    }
}
