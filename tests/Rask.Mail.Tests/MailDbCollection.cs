namespace Rask.Mail.Tests;

/// <summary>
///     The classes that build a <see cref="MailDbContext" />, run as one xUnit collection so they do
///     not run in parallel with each other. See <c>Rask.Jobs.Tests.JobsDbCollection</c> for why the
///     shape is a race — EF Core's model cache is per process and keyed on the context type, so a
///     per-class <c>ServiceProvider</c> over a per-class SQLite file still shares one <c>IModel</c>.
/// </summary>
/// <remarks>
///     <c>MailUnitTests</c> is deliberately not in the collection: it names <c>MailDbContext</c> only
///     as the type argument to <c>AddRaskMail</c> while asserting option validation, and never builds
///     a context.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class MailDbCollection
{
    public const string Name = "mail-db";
}
