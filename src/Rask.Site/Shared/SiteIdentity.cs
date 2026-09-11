namespace Rask.Site;

/// <summary>
///     What the site says about itself, stated once.
/// </summary>
/// <remarks>
///     The front door's title and description used to be written out twice — as <c>App</c>'s fallback and
///     again on <c>HomePage</c> — and the two had already drifted apart by a comma. They now have four
///     readers: those two, the structured data every page carries, and <c>llms.txt</c>. A site that
///     describes itself one way to a search engine and another way to an AI assistant is telling one of
///     them something that is no longer true.
/// </remarks>
public static class SiteIdentity
{
    /// <summary>The product's name, as every title suffix and structured-data node spells it.</summary>
    public const string Name = "Rask";

    /// <summary>
    ///     The front door's title: the name, the identity, and the words someone looking for it types.
    /// </summary>
    /// <remarks>
    ///     Name first, because this is the one page whose query is the name. "C# web apps" is there for
    ///     everyone who has not heard of it yet — and to separate it from the other things called Rask.
    /// </remarks>
    public const string Title = "Rask — the .NET One Person Framework for C# web apps";

    /// <summary>
    ///     The site's description, inside the ~160 characters a search result shows before it cuts.
    /// </summary>
    /// <remarks>
    ///     It was 250 characters, so every result for the front door ended mid-sentence at "the same
    ///     components run on…". <c>PageMetaTests</c> holds every page's description to that budget.
    /// </remarks>
    public const string Description =
        "Rask is the .NET One Person Framework: build, run and ship a whole C# web app — UI, data, auth, "
        + "jobs and deploy — from one codebase on one server.";

    /// <summary>The author, as the NuGet packages name him — the legal name, deliberately.</summary>
    public const string Author = "Tamás Pál";

    /// <summary>Where the author is found.</summary>
    public const string AuthorUrl = "https://github.com/pal-tamas";

    /// <summary>The source repository.</summary>
    public const string Repository = "https://github.com/pal-tamas/rask";

    /// <summary>The one package an application references.</summary>
    public const string Package = "https://www.nuget.org/packages/Rask";

    /// <summary>The licence, as a URL — which is the form schema.org's <c>license</c> expects.</summary>
    public const string License = "https://opensource.org/licenses/MIT";
}
