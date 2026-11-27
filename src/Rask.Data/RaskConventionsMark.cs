using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Rask.Data;

/// <summary>
///     Marks a model as built with Rask.Data's model conventions while it is built, and takes the mark off as it is
///     finalized, so it never reaches a migration's snapshot.
/// </summary>
/// <remarks>
///     What <c>UseRaskSqlServer</c>'s own Guid-order convention reads to leave such a model to Rask.Data's
///     (<see cref="SqlServerGuidOrderConvention" />). Its finalizing step comes after that one's: EF Core puts a
///     provider plugin's conventions before those a context's <c>ConfigureConventions</c> adds.
/// </remarks>
internal sealed class RaskConventionsMark : IModelInitializedConvention, IModelFinalizingConvention
{
    public void ProcessModelInitialized(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context) =>
        modelBuilder.HasAnnotation(SqlServerGuidOrderConvention.RaskConventionsAnnotation, true);

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context) =>
        modelBuilder.HasNoAnnotation(SqlServerGuidOrderConvention.RaskConventionsAnnotation);
}
