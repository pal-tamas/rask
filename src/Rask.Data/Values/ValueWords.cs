using System.ComponentModel;

namespace Rask.Data;

/// <summary>
///     How the rules of <see cref="Text" />, <see cref="Number" />, <see cref="Whole" />, <see cref="Day" />, <see cref="Moment" /> and
///     <see cref="Range" /> are worded. An app's generated module initializer hands over Rask's strings catalog, so
///     they are said in the reader's language; without one they are said in English.
/// </summary>
/// <remarks>
///     The catalog lives in Rask.Core, which the server build of Rask.Data does not reference — so the app, which
///     has both, connects them once as it loads. Called by generated code; not an API to write against.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ValueWords
{
    private static Func<ValueProblem, string>? _say;

    /// <summary>Words every rule from now on with <paramref name="say" />.</summary>
    /// <param name="say">
    ///     Words one problem in the reader's language. Never asked about one that carries the value object's own
    ///     message (<c>Text(Message: …)</c>).
    /// </param>
    public static void Use(Func<ValueProblem, string> say)
    {
        ArgumentNullException.ThrowIfNull(say);
        Volatile.Write(ref _say, say);
    }

    /// <summary>What <paramref name="problem" /> says: its own message, else the catalog's words, else the English.</summary>
    internal static string Say(ValueProblem problem) =>
        problem.Message ?? (Volatile.Read(ref _say) is { } say ? say(problem) : problem.English);
}
