using Microsoft.EntityFrameworkCore.Migrations;

namespace Rask.Data;

/// <summary>A migration's data moved in C#, over the app's own model, rather than in SQL: <c>b.Data(async () =&gt; …)</c>.</summary>
public static class DataMigrationExtensions
{
    /// <summary>
    ///     Adds a data step to the migration: C# that moves or fills the data the migration's schema change needs, run
    ///     through the app's aggregates once every schema step is applied.
    /// </summary>
    /// <param name="migrationBuilder">The migration's <c>Up</c> builder.</param>
    /// <param name="step">The step: <c>async () =&gt; await Tank.UpdateEach(_ =&gt; true, t =&gt; t.Draw(…))</c>.</param>
    /// <remarks>
    ///     <para>
    ///         The step runs after the whole schema is migrated — at the app's start, or <c>rask db migrate</c> — with
    ///         the model the app has now, so it reads and writes what the app does and no SQL is written for it. It runs
    ///         once: the database records each migration whose steps have run, in <c>__RaskDataSteps</c>, and a database
    ///         migrated by <c>dotnet ef</c> alone runs the steps it has not had the next time the app migrates.
    ///     </para>
    ///     <para>
    ///         Write a step so a second run is harmless: the schema step and its data step are not one transaction, so a
    ///         step that fails stops the start, and runs again on the next. The model is today's, so a step names only what
    ///         the model still has — delete it (the migration stays) in the change that removes a column it reads.
    ///     </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// protected override void Up(MigrationBuilder b) => b.Data(async () =&gt;
    ///     await Tank.UpdateEach(_ =&gt; true, async t =&gt; t.Draw(await Pistol.Where(p =&gt; p.TankId == t.Id).Select(p =&gt; p.Id))));
    /// </code>
    /// </example>
    public static void Data(this MigrationBuilder migrationBuilder, Func<Task> step)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentNullException.ThrowIfNull(step);

        migrationBuilder.Operations.Add(DataStep.Holding(step));
    }
}
