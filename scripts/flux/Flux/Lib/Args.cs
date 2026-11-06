namespace Rask.Flux;

/// <summary>A command's arguments, read as the scripts read theirs: <c>--name</c> flags and <c>--name value</c> options.</summary>
public sealed class Args(string[] all)
{
    /// <summary>Every argument, in order.</summary>
    public IReadOnlyList<string> All { get; } = all;

    /// <summary>Whether <c>--<paramref name="name" /></c> was given.</summary>
    public bool Flag(string name) => All.Contains($"--{name}");

    /// <summary>The argument after <c>--<paramref name="name" /></c>, or null.</summary>
    public string? Option(string name)
    {
        var at = IndexOf($"--{name}");
        return at >= 0 && at + 1 < All.Count ? All[at + 1] : null;
    }

    /// <summary>The arguments that are not <c>--</c> flags.</summary>
    public IReadOnlyList<string> Positional => All.Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToList();

    private int IndexOf(string value)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i] == value)
            {
                return i;
            }
        }

        return -1;
    }
}
