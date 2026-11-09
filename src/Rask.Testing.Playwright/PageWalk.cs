using System.Runtime.CompilerServices;

namespace Rask.Testing;

/// <summary>
///     A walk through one page, run when it is awaited: <c>await site.Walk("/orders")</c>, or signed in —
///     <c>await site.Walk("/orders").As(site.Admin)</c>.
/// </summary>
/// <remarks>
///     It throws a <see cref="WalkException" /> listing everything it found wrong. A test method may return it as its
///     <see cref="Task" />: <c>public Task Every_page_opens(string path) => site.Walk(path);</c>
/// </remarks>
public sealed class PageWalk
{
    private readonly Func<string?, Task> _walk;
    private readonly string? _user;

    internal PageWalk(Func<string?, Task> walk, string? user)
    {
        _walk = walk;
        _user = user;
    }

    /// <summary>The page is opened signed in as <paramref name="name" />, the way <c>site.SignedIn(name)</c> opens one.</summary>
    /// <param name="name">Who walks it.</param>
    /// <returns>The same walk, signed in.</returns>
    public PageWalk As(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new PageWalk(_walk, name);
    }

    /// <summary>Runs the walk.</summary>
    /// <returns>The walk's awaiter.</returns>
    public TaskAwaiter GetAwaiter() => ToTask().GetAwaiter();

    /// <summary>Runs the walk.</summary>
    /// <returns>A task that completes once the page has been walked, and fails with a <see cref="WalkException" />.</returns>
    public Task ToTask() => _walk(_user);

    /// <summary>Runs the walk: a test method can return it as its task.</summary>
    /// <param name="walk">The walk to run.</param>
    public static implicit operator Task(PageWalk walk)
    {
        ArgumentNullException.ThrowIfNull(walk);
        return walk.ToTask();
    }
}
