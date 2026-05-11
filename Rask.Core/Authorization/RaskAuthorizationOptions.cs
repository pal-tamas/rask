namespace Rask.Core.Authorization;

public sealed class RaskAuthorizationOptions
{
    public string ChallengePath { get; set; } = "/login";
    public string ForbidPath { get; set; } = "/forbidden";
}
