namespace Rask.Core.Browser;

/// <summary>The user account a passkey is being created for.</summary>
/// <param name="Id">Opaque, stable user handle as base64url (not an email; max 64 bytes decoded).</param>
/// <param name="Name">Account identifier shown in the UI (often an email or username).</param>
/// <param name="DisplayName">Friendly display name.</param>
public sealed record PublicKeyCredentialUser(string Id, string Name, string DisplayName);
