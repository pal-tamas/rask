namespace Rask.Data;

/// <summary>
///     A write to a tenant-scoped table was refused because the application's tenant resolver named no tenant.
/// </summary>
/// <remarks>
///     <para>
///         Thrown only in an app that registered a resolver with <c>AddRaskTenant</c>. There, work the resolver
///         names no tenant for — a request on a host no tenant owns — READS a tenant-scoped table as empty, and
///         is refused here when it tries to write one: a row saved for nobody would be a row nobody can read.
///     </para>
///     <para>
///         It is an <see cref="InvalidOperationException" />, as the refusal in an app without a resolver is, so
///         code that already catches that keeps working.
///     </para>
/// </remarks>
public sealed class MissingTenantException : InvalidOperationException
{
    /// <summary>Creates the exception with a default message.</summary>
    public MissingTenantException()
        : this("No tenant was resolved for this work, so a tenant-scoped table cannot be written.")
    {
    }

    /// <summary>Creates the exception with <paramref name="message" />.</summary>
    /// <param name="message">What was refused, and how to say which tenant.</param>
    public MissingTenantException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with <paramref name="message" /> and the exception that caused it.</summary>
    /// <param name="message">What was refused, and how to say which tenant.</param>
    /// <param name="innerException">The exception that led to this one.</param>
    public MissingTenantException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The refusal of a write to <paramref name="table" />.</summary>
    internal static MissingTenantException ForWrite(string table) =>
        new($"'{table}' is tenant-scoped, and the tenant resolver registered with AddRaskTenant named no " +
            "tenant for this work, so the write is refused — a row saved for nobody is a row nobody can read. " +
            "Return the tenant from the resolver, or open Tenant.Use(id) around the write.");
}
