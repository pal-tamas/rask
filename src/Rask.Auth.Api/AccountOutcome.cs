using System.Security.Claims;
using Rask.Wire;

namespace Rask.Auth;

/// <summary>What an account operation produced: the outcome, and the principal when it succeeded.</summary>
internal sealed record AccountOutcome(AuthResult Result, ClaimsPrincipal? Principal);
