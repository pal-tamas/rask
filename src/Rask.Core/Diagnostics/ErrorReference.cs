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

    // No vowels, so no words, and none of the pairs a person misreads aloud: 0/O, 1/I/L, U/V.
    private const string Alphabet = "BCDFGHJKMNPQRSTWXZ23456789";
    private const int Length = 8;

    private static readonly ConditionalWeakTable<Exception, string> Known = [];

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

        RaskDiagnostics.Report(
            RaskLogLevel.Error, Category, $"An unhandled exception reached {reached}. Reference {fresh}", thrown);
        return fresh;
    }
}
