using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Rask.Data;

/// <summary>On SQL Server, gives every text column the model did not give a collation of its own the app's one.</summary>
/// <remarks>
///     As the model is finalized, so it reaches every battery's table and every entity's own <c>Configure</c> — and at
///     convention strength, so a <c>UseCollation</c> stated explicitly is left as it was.
/// </remarks>
internal sealed class TextOrderConvention(string collation, bool applies) : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        // The name is SQL Server's: another database would refuse it, or read it as one of its own.
        if (!applies)
        {
            return;
        }

        var texts = modelBuilder.Metadata.GetEntityTypes()
            .Where(static e => e.GetTableName() is not null)
            .SelectMany(static e => e.GetDeclaredProperties())
            .Where(static p => p.ClrType == typeof(string));

        foreach (var property in texts)
        {
            property.Builder.UseCollation(collation);
        }
    }
}
