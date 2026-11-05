namespace Rask.Auth.SignInName.Tests;

/// <summary>
///     The classes that build a <see cref="NameDb" />, run as one xUnit collection so they do not run in parallel with
///     each other: EF Core's model cache is per process and keyed on the context type, as <c>AuthDbCollection</c> says.
/// </summary>
[CollectionDefinition(Name)]
public sealed class NameDbCollection
{
    public const string Name = "name-db";
}
