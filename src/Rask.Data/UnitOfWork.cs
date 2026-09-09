using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     One short-lived <see cref="DbContext" />, ambient for the async flow that opened it, committed by
///     a single <see cref="SaveChangesAsync" />.
/// </summary>
/// <remarks>
///     <para>
///         Open one with <see cref="Db.Begin" /> and dispose it with <c>await using</c>. Everything the
///         active-record surface does inside it — <c>Product.Add</c>, <c>Product.Where(…)</c>,
///         <c>entity.SaveAsync()</c> — runs against this one context and commits together.
///     </para>
///     <para>
///         Disposing without saving is not a failure mode to guard against; it is how a unit of work that
///         threw part-way leaves nothing behind. EF Core writes on <c>SaveChanges</c> and nowhere else,
///         so an un-saved context is a discarded one.
///     </para>
/// </remarks>
public sealed class UnitOfWork : IAsyncDisposable, IDisposable
{
    private readonly Func<DbContext>? _factory;
    private readonly Action<UnitOfWork>? _exit;
    private readonly UnitOfWork? _root;

    private DbContext? _context;
    private bool _disposed;

    private UnitOfWork(Func<DbContext>? factory, Action<UnitOfWork>? exit, UnitOfWork? root)
    {
        _factory = factory;
        _exit = exit;
        _root = root;
    }

    /// <summary>
    ///     Whether this handle owns its context — <c>true</c> for the outermost
    ///     <see cref="Db.Begin" />, <c>false</c> for one that joined an outer unit of work.
    /// </summary>
    /// <remarks>
    ///     A joining handle saves and disposes nothing: its <see cref="SaveChangesAsync" /> is a no-op
    ///     returning 0, so a helper that opens a unit of work unconditionally cannot commit half of its
    ///     caller's transaction behind the caller's back.
    /// </remarks>
    public bool IsRoot => _root is null;

    /// <summary>
    ///     The context, created on first access so a unit of work around code that touches no data costs
    ///     nothing.
    /// </summary>
    public DbContext Context
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_root is not null)
            {
                return _root.Context;
            }

            return _context ??= _factory!();
        }
    }

    /// <summary>Whether the context has actually been created yet.</summary>
    internal bool IsMaterialized => _root?.IsMaterialized ?? _context is not null;

    /// <summary>Writes everything tracked by this unit of work, in one transaction.</summary>
    /// <returns>
    ///     The number of state entries written — 0 when nothing was tracked, and 0 for a handle that
    ///     joined an outer unit of work (see <see cref="IsRoot" />).
    /// </returns>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // A joining handle must not commit its caller's transaction, and an untouched root has no
        // context to commit — creating one here just to save nothing would open a connection per
        // wrapped method that happened not to read anything.
        return _root is not null || _context is null
            ? Task.FromResult(0)
            : _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <b>Deliberately not an <c>async</c> method.</b> Leaving the ambient scope writes an
    ///     <see cref="AsyncLocal{T}" />, and an <c>async</c> body's writes land on the state machine's own
    ///     execution context, never reaching the caller's — so the scope would look open forever and the
    ///     next <c>Db.Begin()</c> would hand out a handle onto this disposed context. Popping happens in
    ///     the synchronous body; only the context's disposal is awaited.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _exit?.Invoke(this);

        var context = _context;
        _context = null;
        return context?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _exit?.Invoke(this);
        _context?.Dispose();
        _context = null;
    }

    internal static UnitOfWork Owning(Func<DbContext> factory, Action<UnitOfWork> exit) =>
        new(factory, exit, root: null);

    internal static UnitOfWork Joining(UnitOfWork root) =>
        new(factory: null, exit: null, root.IsRoot ? root : root._root!);
}
