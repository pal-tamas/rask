namespace Rask.Printing;

/// <summary>How the app's Chromium is found and how hard it may be worked; bound from <c>Rask:Pdf</c>.</summary>
internal sealed class ChromiumSettings
{
    /// <summary>The Chromium or Google Chrome binary. Unset, the usual install locations and <c>PATH</c> are searched.</summary>
    public string? ChromiumPath { get; set; }

    /// <summary>
    /// Starts Chromium with <c>--no-sandbox</c>. Only for a container that runs as root or without the user
    /// namespaces Chromium's sandbox needs; the documents printed are the app's own, never a URL from a user.
    /// </summary>
    public bool NoSandbox { get; set; }

    /// <summary>
    /// How many documents print at once; the rest wait their turn. Memory is the bound, not cores: laying out a
    /// 5,000-row table (209 A4 pages) peaked its renderer at about 1.3 GB.
    /// </summary>
    public int MaxConcurrentPrints { get; set; } = 2;

    /// <summary>The longest document that prints; a longer one fails rather than come out cut short.</summary>
    public int MaxPages { get; set; } = 1000;

    /// <summary>How long one document may take, from loading its HTML to the last byte of the PDF.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Refuses a setting no printer could run with, naming it.</summary>
    internal void Validate()
    {
        if (MaxConcurrentPrints < 1)
        {
            throw new InvalidOperationException("Rask:Pdf:MaxConcurrentPrints must be at least 1.");
        }

        if (MaxPages < 1)
        {
            throw new InvalidOperationException("Rask:Pdf:MaxPages must be at least 1.");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Rask:Pdf:Timeout must be longer than zero.");
        }
    }
}
