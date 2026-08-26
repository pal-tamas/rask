namespace Rask.Data;

/// <summary>
/// Thrown when a save violates a rule declared by
/// <see cref="RangeExclusionBuilderExtensions.HasNonOverlappingRange{TEntity}"/> — the row's range overlaps one
/// already stored.
/// </summary>
/// <remarks>
/// This is a rejected write, not a fault: a booking screen should surface it as "that slot is already taken".
/// Providers translate their native constraint error into this so callers never match on store error codes.
/// </remarks>
public sealed class RangeOverlapException : Exception
{
    /// <summary>Creates the exception with the default message.</summary>
    public RangeOverlapException()
        : this("The range overlaps an existing row.", null, string.Empty)
    {
    }

    /// <summary>Creates the exception with a supplied message.</summary>
    /// <param name="message">The message.</param>
    public RangeOverlapException(string message)
        : this(message, null, string.Empty)
    {
    }

    /// <summary>Creates the exception with a supplied message and cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public RangeOverlapException(string message, Exception? innerException)
        : this(message, innerException, string.Empty)
    {
    }

    private RangeOverlapException(string message, Exception? innerException, string table)
        : base(message, innerException)
        => Table = table;

    /// <summary>The table whose rule was violated, or empty when the provider could not name it.</summary>
    public string Table { get; }

    /// <summary>Creates the exception for a named table, which is what providers use when translating.</summary>
    /// <param name="table">The table whose rule was violated.</param>
    /// <param name="innerException">The provider error being translated.</param>
    /// <returns>An exception naming <paramref name="table"/>.</returns>
    public static RangeOverlapException ForTable(string table, Exception? innerException = null)
        => new(
            string.IsNullOrEmpty(table)
                ? "The range overlaps an existing row."
                : $"The range overlaps an existing row in '{table}'.",
            innerException,
            table ?? string.Empty);
}
