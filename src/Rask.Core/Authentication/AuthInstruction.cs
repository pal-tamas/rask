namespace Rask.Core.Authentication;

public sealed record AuthInstruction(string Ticket, string? ReturnUrl);
