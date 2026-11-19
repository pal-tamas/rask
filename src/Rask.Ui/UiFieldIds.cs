using System.Globalization;
using System.Runtime.CompilerServices;
using Rask.Core.Live;

namespace Rask;

/// <summary>
///     The field ids one page has handed out, so two fields that derive the same id — two forms with a
///     <c>Name</c> — do not both take it: the first keeps <c>f-name</c>, the next is <c>f-name-2</c>, then <c>-3</c>.
/// </summary>
/// <remarks>
///     <para>
///         Kept per page — per root of the render walk, which a live page keeps for its whole session on either host —
///         and held by the field's INSTANCE, not by its place in the walk: a field keeps the id it was given for as
///         long as it is on the page, so a live patch never re-ids it (and never takes the focus out of it), and a
///         subtree served from the render cache, whose fields do not ask again, keeps its ids taken.
///     </para>
///     <para>
///         A new field takes the first id no field still on the page holds. A field's id is free again once it has
///         left the page (unmounted), or when it never rendered at all (an instance a chain built and dropped).
///         The first render of a page hands ids out in render order, which is the same order on the server's first
///         HTML and on the render that takes it over, so the two agree.
///     </para>
///     <para>
///         An id the call site gives is never changed. It is held as well, so a field that derives that id later
///         steps past it.
///     </para>
/// </remarks>
internal sealed class UiFieldIds
{
    private static readonly ConditionalWeakTable<Component, UiFieldIds> Pages = [];

    private readonly Dictionary<string, Claim> _held = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<Component, Claim> _claims = [];

    // Where the walk in flight left off for each derived id: the fields after the first that derive one, all new in
    // one walk — a list's rows — start looking where the last one stopped instead of at the first id again.
    private readonly Dictionary<string, (long Walk, int Next)> _cursors = new(StringComparer.Ordinal);

    private readonly Component _root;

    private UiFieldIds(Component root) => _root = root;

    /// <summary>The id <paramref name="owner" /> derived as <paramref name="wanted" />, made unique on its page.</summary>
    internal static string Derived(Component? owner, string wanted) =>
        owner is not null && For() is { } page ? page.Take(owner, wanted, derived: true) : wanted;

    /// <summary>Holds an id the call site gave <paramref name="owner" />, unchanged, so no derived id takes it.</summary>
    internal static string Given(Component? owner, string id) =>
        owner is not null && For() is { } page ? page.Take(owner, id, derived: false) : id;

    // The page the walk in flight renders; none outside a render, where an id is the plain derived one.
    private static UiFieldIds? For() =>
        (LiveRenderContext.CurrentSync ?? LiveRenderContext.Current) is { } walk
            ? Pages.GetValue(walk.Root, static root => new UiFieldIds(root))
            : null;

    private long CurrentWalk => _root.RenderGenerationInternal;

    private string Take(Component owner, string wanted, bool derived)
    {
        var walk = CurrentWalk;
        if (_claims.TryGetValue(owner, out var claim))
        {
            if (string.Equals(claim.Wanted, wanted, StringComparison.Ordinal) && claim.IsDerived == derived)
            {
                claim.Walk = walk;
                return claim.Id;
            }

            // Named again by something else — its label changed: what it held is free.
            Release(claim);
        }

        var id = derived ? FirstFree(owner, wanted, walk) : wanted;
        claim = new Claim(new WeakReference<Component>(owner), wanted, id, derived) { Walk = walk };
        _claims.AddOrUpdate(owner, claim);
        if (derived || IsFree(id, owner, walk))
        {
            _held[id] = claim;
        }

        return id;
    }

    private string FirstFree(Component owner, string wanted, long walk)
    {
        var n = _cursors.TryGetValue(wanted, out var cursor) && cursor.Walk == walk ? cursor.Next : 1;
        while (!IsFree(Numbered(wanted, n), owner, walk))
        {
            n++;
        }

        _cursors[wanted] = (walk, n + 1);
        return Numbered(wanted, n);
    }

    private static string Numbered(string wanted, int n) =>
        n == 1 ? wanted : wanted + "-" + n.ToString(CultureInfo.InvariantCulture);

    private bool IsFree(string id, Component owner, long walk) =>
        !_held.TryGetValue(id, out var holder)
        || !holder.Owner.TryGetTarget(out var held)
        || ReferenceEquals(held, owner)
        || held.IsTornDown
        || (!held.HasInitializedInternal && holder.Walk != walk);

    private void Release(Claim claim)
    {
        if (_held.TryGetValue(claim.Id, out var holder) && ReferenceEquals(holder, claim))
        {
            _held.Remove(claim.Id);
        }
    }

    // What one field holds: the id it asked for, the one it was given, and the last walk that asked.
    private sealed class Claim(WeakReference<Component> owner, string wanted, string id, bool derived)
    {
        internal WeakReference<Component> Owner { get; } = owner;

        internal string Wanted { get; } = wanted;

        internal string Id { get; } = id;

        internal bool IsDerived { get; } = derived;

        internal long Walk { get; set; }
    }
}
