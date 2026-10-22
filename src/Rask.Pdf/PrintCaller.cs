using System.Globalization;
using System.Security.Claims;
using Rask.Core.Globalization;

namespace Rask.Printing;

/// <summary>Who a PDF is printed for: their user, their language, and the page numbers said in it.</summary>
/// <param name="User">The page is authorized and rendered for them.</param>
/// <param name="Culture">The language of the work in progress, or null when the app configured no languages.</param>
/// <param name="PageNumbers">The foot's page numbers as HTML, in that language.</param>
internal sealed record PrintCaller(ClaimsPrincipal User, CultureInfo? Culture, string PageNumbers)
{
    /// <summary>Read from the work in progress, before anything is awaited.</summary>
    public static PrintCaller Of(ClaimsPrincipal user) =>
        new(user, RaskCulture.IsEnabled ? RaskCulture.CurrentUI : null, PageNumberFoot.Html());
}
