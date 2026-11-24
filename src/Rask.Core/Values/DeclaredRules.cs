namespace Rask.Values;

/// <summary>
///     Reads the rules a value object hands its base, by making one while a read is open: the base notes what it was
///     given and checks nothing, so a sample that satisfies the rules can be built from them (<c>Test.Make&lt;T&gt;()</c>).
/// </summary>
/// <remarks>Per thread: a constructor runs on the thread that calls it, and a read never awaits.</remarks>
internal static class DeclaredRules
{
    [ThreadStatic]
    private static bool t_reading;

    [ThreadStatic]
    private static IValueRules? t_noted;

    /// <summary>Whether a read is open on this thread: a base notes its rules only then.</summary>
    public static bool IsReading => t_reading;

    /// <summary>The rules the base of what <paramref name="make" /> makes was handed, or null for one declared from <see cref="Value" />.</summary>
    /// <param name="make">Makes the value object, from any arguments: nothing it is given is checked.</param>
    /// <returns>What its base noted.</returns>
    public static IValueRules? Read(Action make)
    {
        ArgumentNullException.ThrowIfNull(make);

        var wasReading = t_reading;
        var wasNoted = t_noted;
        t_reading = true;
        t_noted = null;
        try
        {
            using (RowLoading.Begin())
            {
                make();
            }

            return t_noted;
        }
        finally
        {
            t_reading = wasReading;
            t_noted = wasNoted;
        }
    }

    /// <summary>Records what a base was handed, for the read in flight.</summary>
    /// <param name="rules">The base's rules.</param>
    public static void Note(IValueRules rules) => t_noted = rules;
}
