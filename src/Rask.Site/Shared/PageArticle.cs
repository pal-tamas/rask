namespace Rask.Site;

/// <summary>
///     What a guide adds to the head beyond what every page carries.
/// </summary>
/// <param name="Section">
///     The catalog group the guide sits in — <c>article:section</c>, and the article's
///     <c>articleSection</c> in the structured data.
/// </param>
/// <param name="Modified">
///     When the guide's source last changed, as git recorded it; <c>null</c> when the build could not say,
///     in which case no date is claimed anywhere.
/// </param>
/// <param name="MarkdownUrl">The absolute URL of the guide's Markdown twin, advertised as an alternate.</param>
public sealed record PageArticle(string Section, DateOnly? Modified, string MarkdownUrl);
