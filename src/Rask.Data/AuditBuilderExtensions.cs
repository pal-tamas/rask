using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>Where the model's <see cref="Audited" /> aggregates leave their audit rows, when not in Rask's <see cref="AuditEntry" />.</summary>
public static class AuditBuilderExtensions
{
    /// <summary>
    ///     Records every save of the model's <see cref="Audited" /> aggregates as <paramref name="format" />'s rows, in the
    ///     app's own audit table: <c>m.AuditAs(new LegacyAudits())</c>, in the app's <c>IRaskModel.Model</c>.
    /// </summary>
    /// <typeparam name="TRow">The app's audit row, mapped by this call and by its own <c>Configure</c>.</typeparam>
    /// <typeparam name="TUserId">What the app's users are keyed by.</typeparam>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="format">Writes each save's row.</param>
    /// <returns>The same model builder.</returns>
    /// <remarks>
    ///     <see cref="AuditEntry" /> is then left out of the model, so its <c>Audits</c> table is the app's to map:
    ///     <c>builder.ToTable("Audits")</c> in <typeparamref name="TRow" />'s <c>Configure</c>.
    /// </remarks>
    public static ModelBuilder AuditAs<[DynamicallyAccessedMembers(DataTrimming.Entity)] TRow, TUserId>(
        this ModelBuilder modelBuilder, AuditFormat<TRow, TUserId> format)
        where TRow : class
        where TUserId : struct
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(format);

        modelBuilder.Model.SetAnnotation(AuditEntries.FormatAnnotation, format);
        modelBuilder.Ignore<AuditEntry>();
        modelBuilder.Entity<TRow>();
        return modelBuilder;
    }
}
