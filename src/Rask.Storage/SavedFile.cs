namespace Rask.Storage;

/// <summary>One file a test saved.</summary>
/// <param name="Id">Its id, as the code under test received it.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Size">How many bytes were stored.</param>
/// <param name="IsPublic">Whether <c>.Public()</c> was asked for.</param>
public sealed record SavedFile(Guid Id, string Name, long Size, bool IsPublic);
