namespace Rask.Site.Features;

public sealed class BannedWordService : IBannedWordService
{
    public IReadOnlyCollection<string> Words { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "admin", "root", "test" };
}
