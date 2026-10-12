using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Rask.Core.Diagnostics;

/// <summary>
///     The short reference a visitor can quote for an exception they were never shown, and the Error log
///     line that carries the same reference beside the exception itself.
/// </summary>
/// <remarks>
///     One reference per exception object, logged once, however many boundaries and renders it passes
///     through. Random, never derived from the exception or the visitor, so it says nothing by itself.
/// </remarks>
internal static class ErrorReference
{
    internal const string Category = "Rask.Errors";

    private const string English = "The problem has been recorded. If it keeps happening, quote this reference: {0}";

    // No vowels, so no words, and none of the pairs a person misreads aloud: 0/O, 1/I/L, U/V.
    private const string Alphabet = "BCDFGHJKMNPQRSTWXZ23456789";
    private const int Length = 8;

    private static readonly ConditionalWeakTable<Exception, string> Known = [];

    // Set inside the prerender's own async flow, so it reaches the hooks that flow starts and nothing else.
    private static readonly AsyncLocal<bool> ForNobody = new();

    /// <summary>The reference of <paramref name="thrown" />, logging it with the exception the first time it is asked for.</summary>
    internal static string Of(Exception thrown) => Of(thrown, "an error page");

    /// <summary>The same, naming <paramref name="reached" /> — what the exception got to — in the log line.</summary>
    internal static string Of(Exception thrown, string reached)
    {
        if (Known.TryGetValue(thrown, out var known))
        {
            return known;
        }

        var fresh = RandomNumberGenerator.GetString(Alphabet, Length);
        if (!Known.TryAdd(thrown, fresh))
        {
            return Known.TryGetValue(thrown, out known) ? known : fresh;
        }

        Report(thrown, reached, fresh);
        return fresh;
    }

    /// <summary>What a visitor reads in place of <paramref name="thrown" />: that it was recorded, and its reference.</summary>
    internal static string Sentence(Exception thrown, string reached = "an error page") =>
        Globalization.RaskStrings.Get(Globalization.RaskString.ErrorReference, English, Of(thrown, reached));

    /// <summary>
    ///     Marks the calling flow as a build-time render: nobody is shown its pages, so what a boundary takes
    ///     there is said at Information, in one line.
    /// </summary>
    /// <remarks>
    ///     A prerender pass has no browser, so a hook that reaches for one faults on every page that has it —
    ///     after the page was taken, and without harm. The pass itself reports a page it could not write.
    /// </remarks>
    internal static void RenderingForNobody() => ForNobody.Value = true;

    private static void Report(Exception thrown, string reached, string reference)
    {
        if (ForNobody.Value)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Information, Category,
                $"While prerendering, an exception reached {reached}: {thrown.GetType().Name}: {thrown.Message}");
            return;
        }

        RaskDiagnostics.Report(
            RaskLogLevel.Error, Category, $"An unhandled exception reached {reached}. Reference {reference}", thrown);
    }
}
