namespace Rask;

/// <summary>Numbers the instances of the kit's GENERIC controls, for the ids they announce themselves by.</summary>
/// <remarks>
///     Outside the generic type on purpose: a static counter inside <c>UiTree&lt;T, TKey&gt;</c> is one counter per
///     closed type, so a tree of files and a tree of people on one page would both be number 1 and collide.
/// </remarks>
internal static class UiInstanceCounter
{
    private static int _last;

    /// <summary>The next number, unique across every generic control in the process.</summary>
    internal static int Next() => Interlocked.Increment(ref _last);
}
