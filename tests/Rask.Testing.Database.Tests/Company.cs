using Microsoft.EntityFrameworkCore;
using Rask.Cqrs;

namespace Rask.Testing.Database.Tests;

/// <summary>A company, which refuses to go while it still has contacts — and only the database knows whether it does.</summary>
public sealed class Company : Aggregate<Guid, Timestamps, Versioned, Model, SoftDelete>
{
    public const string StillHasContacts = "A company that still has contacts is not removed.";

    private Company() { } // EF materialization

    public string Name { get; private set; } = "";

    public static Company Named(string name) => new() { Id = Guid.NewGuid(), Name = name };

    /// <summary>Removes the company through <paramref name="db" />, unless a contact still belongs to it.</summary>
    public async Task Remove(DbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (await Contact.Where(c => c.CompanyId == Id).Any())
        {
            throw new RaskValidationException(StillHasContacts);
        }

        db.Remove(this);
    }
}

/// <summary>Someone at a company.</summary>
public sealed class Contact : Aggregate<Guid, Timestamps, Versioned, Model, SoftDelete>
{
    private Contact() { } // EF materialization

    public Guid CompanyId { get; private set; }

    public static Contact At(Company company) => new() { Id = Guid.NewGuid(), CompanyId = company.Id };
}
