using System.Security.Cryptography;

namespace Rask.Hosting.Shared;

// The reference a person can quote for an exception they were never shown: the error page's, a toast's,
// a remote dispatch's 500. Random, never derived from the exception or the caller, so it says nothing by
// itself; the Error log line that carries it beside the exception is the caller's to write.
internal static class ErrorReferenceId
{
    /// <summary>The log category every such line is written under, whichever package wrote it.</summary>
    internal const string Category = "Rask.Errors";

    // No vowels, so no words, and none of the pairs a person misreads aloud: 0/O, 1/I/L, U/V.
    private const string Alphabet = "BCDFGHJKMNPQRSTWXZ23456789";
    private const int Length = 8;

    internal static string New() => RandomNumberGenerator.GetString(Alphabet, Length);
}
