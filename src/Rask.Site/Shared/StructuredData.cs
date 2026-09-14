using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Rask.Core.Live;

namespace Rask.Site;

/// <summary>
///     The schema.org JSON-LD graph a page carries in its head.
/// </summary>
/// <remarks>
///     <para>
///         What a crawler cannot infer from the markup: that the site is a piece of software with a
///         repository, a package and a licence; that a guide is a technical article with a date; and where a
///         page sits in the site. The breadcrumb is the part a search result visibly uses — "rask.sh › Docs ›
///         CQRS in .NET" rather than a bare URL.
///     </para>
///     <para>
///         <b>One graph per page, with the shared nodes named by <c>@id</c></b>, so the website, the author
///         and the software are one entity each across the whole site rather than a hundred and fifty
///         unconnected copies.
///     </para>
///     <para>
///         Written with <see cref="Utf8JsonWriter" /> rather than a serializer, because this app publishes
///         trimmed and a reflection-based serializer over anonymous objects is exactly what the trimmer takes
///         apart. The writer's default encoder escapes <c>&lt;</c>, so no string in the graph can close the
///         <c>&lt;script&gt;</c> it is embedded in.
///     </para>
/// </remarks>
public static class StructuredData
{
    /// <summary>One step of a page's breadcrumb trail.</summary>
    public readonly record struct Crumb(string Name, string Url);

    /// <summary>Everything the graph says about one page.</summary>
    /// <param name="Name">The page's name — its title without the site suffix.</param>
    /// <param name="Description">The page's meta description.</param>
    /// <param name="Url">The page's canonical URL.</param>
    /// <param name="IsHome">Whether this is the front door, which alone describes the software itself.</param>
    /// <param name="Breadcrumb">The trail from the site root to the page; empty for the front door.</param>
    /// <param name="Article">The guide facts, or <c>null</c> for a page that is not an article.</param>
    public readonly record struct Page(
        string Name,
        string Description,
        string Url,
        bool IsHome,
        IReadOnlyList<Crumb> Breadcrumb,
        PageArticle? Article);

    private static string Root => PageMeta.Origin + LiveOptions.PathBase + "/";

    internal static string WebsiteId => Root + "#website";

    internal static string AuthorId => Root + "#author";

    internal static string SoftwareId => Root + "#software";

    /// <summary>The page's graph, as the JSON text of a <c>&lt;script type="application/ld+json"&gt;</c>.</summary>
    public static string Graph(Page page)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("@context", "https://schema.org");
            json.WriteStartArray("@graph");

            WriteWebsite(json);
            WriteAuthor(json);
            if (page.IsHome)
            {
                WriteSoftware(json);
            }

            WritePage(json, page);
            if (page.Breadcrumb.Count > 0)
            {
                WriteBreadcrumb(json, page);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteWebsite(Utf8JsonWriter json)
    {
        json.WriteStartObject();
        json.WriteString("@type", "WebSite");
        json.WriteString("@id", WebsiteId);
        json.WriteString("url", Root);
        json.WriteString("name", SiteIdentity.Name);
        json.WriteString("description", SiteIdentity.Description);
        json.WriteString("inLanguage", "en");
        Reference(json, "publisher", AuthorId);
        json.WriteEndObject();
    }

    private static void WriteAuthor(Utf8JsonWriter json)
    {
        json.WriteStartObject();
        json.WriteString("@type", "Person");
        json.WriteString("@id", AuthorId);
        json.WriteString("name", SiteIdentity.Author);
        json.WriteString("url", SiteIdentity.AuthorUrl);
        json.WriteEndObject();
    }

    // No aggregateRating and no review. Google shows a software rich result only with one of them, and the
    // only honest source for either would be real ratings this site does not collect — a made-up rating is
    // a manual action waiting to happen. The node is still what tells a crawler, and an assistant reading
    // the graph, that "Rask" here is a free .NET developer tool rather than any of the other things so named.
    private static void WriteSoftware(Utf8JsonWriter json)
    {
        json.WriteStartObject();
        json.WriteString("@type", "SoftwareApplication");
        json.WriteString("@id", SoftwareId);
        json.WriteString("name", SiteIdentity.Name);
        json.WriteString("description", SiteIdentity.Description);
        json.WriteString("url", Root);
        json.WriteString("applicationCategory", "DeveloperApplication");
        json.WriteString("applicationSubCategory", "Web framework");
        json.WriteString("operatingSystem", "Windows, macOS, Linux");
        json.WriteString("softwareRequirements", ".NET 10 SDK or newer");
        json.WriteString("license", SiteIdentity.License);
        json.WriteBoolean("isAccessibleForFree", true);
        json.WriteString("downloadUrl", SiteIdentity.Package);
        json.WriteString("image", PageMeta.SocialImageUrl);
        Reference(json, "author", AuthorId);
        json.WriteStartObject("offers");
        json.WriteString("@type", "Offer");
        json.WriteString("price", "0");
        json.WriteString("priceCurrency", "USD");
        json.WriteEndObject();
        json.WriteStartArray("sameAs");
        json.WriteStringValue(SiteIdentity.Repository);
        json.WriteStringValue(SiteIdentity.Package);
        json.WriteEndArray();
        json.WriteEndObject();

        json.WriteStartObject();
        json.WriteString("@type", "SoftwareSourceCode");
        json.WriteString("@id", Root + "#source");
        json.WriteString("name", SiteIdentity.Name);
        json.WriteString("codeRepository", SiteIdentity.Repository);
        json.WriteString("programmingLanguage", "C#");
        json.WriteString("runtimePlatform", ".NET");
        json.WriteString("license", SiteIdentity.License);
        Reference(json, "author", AuthorId);
        Reference(json, "targetProduct", SoftwareId);
        json.WriteEndObject();
    }

    private static void WritePage(Utf8JsonWriter json, Page page)
    {
        json.WriteStartObject();

        if (page.Article is { } article)
        {
            json.WriteString("@type", "TechArticle");
            json.WriteString("@id", page.Url + "#article");
            json.WriteString("headline", page.Name);
            json.WriteString("mainEntityOfPage", page.Url);
            json.WriteString("articleSection", article.Section);
            json.WriteString("image", PageMeta.SocialImageUrl);
            Reference(json, "author", AuthorId);

            // Only when git said. A dateModified that is really the build date would tell a crawler every
            // page changed on every deploy, which it learns to ignore — for this site and its sitemap alike.
            if (article.Modified is { } modified)
            {
                json.WriteString("dateModified", modified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
        }
        else
        {
            json.WriteString("@type", "WebPage");
            json.WriteString("@id", page.Url + "#webpage");
            json.WriteString("name", page.Name);
        }

        json.WriteString("url", page.Url);
        json.WriteString("description", page.Description);
        json.WriteString("inLanguage", "en");
        Reference(json, "isPartOf", WebsiteId);

        if (page.IsHome)
        {
            Reference(json, "about", SoftwareId);
        }

        if (page.Breadcrumb.Count > 0)
        {
            Reference(json, "breadcrumb", page.Url + "#breadcrumb");
        }

        json.WriteEndObject();
    }

    private static void WriteBreadcrumb(Utf8JsonWriter json, Page page)
    {
        json.WriteStartObject();
        json.WriteString("@type", "BreadcrumbList");
        json.WriteString("@id", page.Url + "#breadcrumb");
        json.WriteStartArray("itemListElement");

        for (var i = 0; i < page.Breadcrumb.Count; i++)
        {
            json.WriteStartObject();
            json.WriteString("@type", "ListItem");
            json.WriteNumber("position", i + 1);
            json.WriteString("name", page.Breadcrumb[i].Name);
            json.WriteString("item", page.Breadcrumb[i].Url);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void Reference(Utf8JsonWriter json, string property, string id)
    {
        json.WriteStartObject(property);
        json.WriteString("@id", id);
        json.WriteEndObject();
    }
}
