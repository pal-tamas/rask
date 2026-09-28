using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

// Resolved from the form's render-scoped IServiceProvider by [NotBanned]'s GetValidationResult.
public interface IBannedWordService
{
    IReadOnlyCollection<string> Words { get; }
}
