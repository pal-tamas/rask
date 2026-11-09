namespace Rask.Testing;

/// <summary>A walk found something wrong on its page: <see cref="Exception.Message" /> lists it, with what the app said.</summary>
public sealed class WalkException : Exception
{
    /// <summary>A walk that failed.</summary>
    public WalkException()
    {
    }

    /// <summary>A walk that failed for <paramref name="message" />.</summary>
    /// <param name="message">What was wrong.</param>
    public WalkException(string message) : base(message)
    {
    }

    /// <summary>A walk that failed for <paramref name="message" />, because of <paramref name="innerException" />.</summary>
    /// <param name="message">What was wrong.</param>
    /// <param name="innerException">What it ran into.</param>
    public WalkException(string message, Exception innerException) : base(message, innerException)
    {
    }

    internal WalkException(string path, string? user, IReadOnlyCollection<string> problems, string said)
        : base(Describe(path, user, problems, said))
    {
        Path = path;
        Problems = problems;
    }

    /// <summary>The page walked.</summary>
    public string Path { get; } = "";

    /// <summary>Each thing found wrong, one sentence each.</summary>
    public IReadOnlyCollection<string> Problems { get; } = [];

    private static string Describe(string path, string? user, IReadOnlyCollection<string> problems, string said)
    {
        var who = user is null ? "signed out" : $"as {user}";
        var lines = string.Join(Environment.NewLine, problems.Select(problem => "  - " + problem));
        return $"Walking {path} {who} found {problems.Count} problem{(problems.Count == 1 ? "" : "s")}:{Environment.NewLine}"
               + $"{lines}{Environment.NewLine}The app said, last:{Environment.NewLine}{said}";
    }
}
