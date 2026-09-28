using Rask.Wire;

namespace Rask.Auth;

/// <summary>Names shared by the auth endpoints and the clients that call them.</summary>
/// <remarks>
/// The contract itself lives in <see cref="AuthApi" />, in Rask.Wire, because the browser half cannot
/// reference this package — it carries Entity Framework, which must not reach a trimmed WebAssembly publish.
/// </remarks>
public static class RaskAuthDefaults
{
    /// <inheritdoc cref="AuthApi.RequestHeader" />
    public const string RequestHeader = AuthApi.RequestHeader;
}
