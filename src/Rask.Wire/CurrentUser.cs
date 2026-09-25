namespace Rask.Wire;

/// <summary>Who is signed in.</summary>
/// <param name="Id">The account's stable id.</param>
/// <param name="Email">The account's email address.</param>
/// <param name="Roles">The roles it holds.</param>
public sealed record CurrentUser(string? Id, string? Email, IReadOnlyList<string> Roles);
