namespace Rask.Auth.Pages;

/// <summary>
/// The bodies of the two emails the account lifecycle sends.
/// </summary>
/// <remarks>
/// Rask components, because <c>Email.Body</c> takes one — an email here is written the same way a page
/// is, rather than in a second templating language that only emails use.
/// <para>
/// Deliberately plain. These are transactional messages read once, often in a client that strips most
/// styling anyway, and the one thing that has to survive is the link. An app that wants its own
/// branding overrides the pages and sends its own.
/// </para>
/// </remarks>
// [RaskMarkup] because this is a static class and so cannot derive from RaskMarkup: the chain's entries
// (P, Div, A…) are members of the enclosing type, and a plain static class has none — RASK043.
[RaskMarkup]
internal static partial class AuthEmails
{
    /// <summary>"Confirm your address."</summary>
    public static Component Confirm(string link, string heading) =>
        Wrap(
            heading,
            P["Thanks for signing up. Confirm this address to finish:"],
            link,
            "Confirm email",
            P.Style(Muted)["If you did not create this account, you can ignore this message."]);

    /// <summary>"Reset your password."</summary>
    public static Component Reset(string link, string heading, TimeSpan lifetime) =>
        Wrap(
            heading,
            P["Somebody asked to reset the password for this address. If it was you:"],
            link,
            "Choose a new password",
            P.Style(Muted)[
                $"This link works once, and expires in about {Describe(lifetime)}. "
                + "If you did not ask for it, nothing has changed and you can ignore this message."]);

    private const string Muted = "color:#666;font-size:0.875rem";

    private static Component Wrap(
        string heading, Component lead, string link, string cta, Component footer) =>
        Div.Style("font-family:system-ui,sans-serif;max-width:32rem")[
            H1.Style("font-size:1.25rem")[heading],
            lead,
            P[
                A.Href(link).Style(
                    "display:inline-block;padding:0.6rem 1rem;border-radius:0.375rem;"
                    + "background:#512BD4;color:#fff;text-decoration:none")[cta]
            ],
            // The same URL in full, because a button is not a link in every client — some strip the
            // anchor, some show the text and hide the href, and a person forwarding this to support
            // needs something they can copy.
            P.Style(Muted)["Or paste this into your browser:"],
            P.Style("word-break:break-all;font-size:0.8125rem")[link],
            Hr,
            footer
        ];

    /// <summary>"2 hours", "30 minutes" — a duration a person reads rather than a TimeSpan.</summary>
    private static string Describe(TimeSpan lifetime) =>
        lifetime.TotalHours >= 1
            ? Plural(Math.Round(lifetime.TotalHours), "hour")
            : Plural(Math.Round(lifetime.TotalMinutes), "minute");

    private static string Plural(double count, string unit) =>
        count == 1 ? $"1 {unit}" : $"{count:0} {unit}s";
}
