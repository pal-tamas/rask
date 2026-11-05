namespace Rask.Core.Authentication;

public sealed record AuthInstruction(string Ticket, string? ReturnUrl)
{
    // Whether the browser loads the page again once the ticket is redeemed, rather than reconnecting: a sign-in into
    // the session cookie, whose user Rask cannot know before the app's own scheme has read the cookie.
    internal bool LoadsPage { get; init; }
}
