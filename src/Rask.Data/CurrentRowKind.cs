namespace Rask.Data;

/// <summary>Which of the app's own rows a <see cref="CurrentRow" /> holds.</summary>
internal enum CurrentRowKind
{
    /// <summary>The tenant's row: the aggregate that says <see cref="ITenant" />, read by <c>Tenant.Key</c>.</summary>
    Tenant,

    /// <summary>The user's row: the aggregate that says <see cref="IUser" />, read by <c>Current.UserId</c>.</summary>
    User,
}
