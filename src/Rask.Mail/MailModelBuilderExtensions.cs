using Microsoft.EntityFrameworkCore;

namespace Rask.Mailing;

/// <summary>Model-building helper for the mail table.</summary>
public static class MailModelBuilderExtensions
{
    /// <summary>
    /// Maps the <see cref="QueuedMail"/> table. Call from your context's <c>OnModelCreating</c>, then create
    /// the schema with <c>rask db add AddMail &amp;&amp; rask db update</c>.
    /// </summary>
    public static ModelBuilder AddRaskMail(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new QueuedMailConfiguration());
        return modelBuilder;
    }
}
