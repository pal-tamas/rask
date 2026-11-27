using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Rask.Data;

/// <summary>
///     Declares a <see cref="TenantReference" /> from every table that <see cref="IBelongsTo{TTenant}" /> the app's tenant
///     — the aggregate that says <see cref="ITenant" /> — to that tenant's key, as the model is finalized: the database
///     refuses to delete a tenant that still has rows, on SQLite as on SQL Server and PostgreSQL.
/// </summary>
/// <remarks>
///     <para>
///         The key is the database's, made by the migrations and by <c>EnsureCreated</c> through <c>UseRaskSqlite</c>,
///         <c>UseRaskSqlServer</c> and <c>UseRaskPostgres</c>: <c>NO ACTION</c> on delete, named by the app's
///         <c>ConstraintNames</c> rule (<see cref="ConstraintKind.ForeignKey" />) or <c>FK_{table}_{tenant}_{column}</c>.
///         It is no relationship of the model — EF Core relates only properties of one type, and a tenant is often a
///         <see cref="Guid" /> to its tables and text to its own row — so a save neither orders its commands by it nor
///         deletes a tenant's rows with it. Give the tenant a <c>WhenReferenced("…")</c> sentence and a refused delete
///         says it.
///     </para>
///     <para>
///         SQLite makes a foreign key only with its table, so there it is in the <c>CREATE TABLE</c> — a database
///         made from the model, a test's — and a migration that adds it to a table SQLite already has adds nothing.
///     </para>
/// </remarks>
internal sealed class TenantReferenceConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context) =>
        TenantReferenceDeclaration.Declare(modelBuilder, name: null);
}
