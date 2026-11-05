namespace Rask.Auth;

/// <summary>
/// Marks the property of the app's <c>User</c> people sign in with, when it is not the address: a user name.
/// </summary>
/// <remarks>
/// <para>
/// Text: a <see cref="string" />, or a value object holding one, such as <c>Username</c>. It is unique — per tenant, as an
/// address is — and compared as it is stored, trimmed of spaces at either end, so the column's collation decides whether
/// <c>Admin</c> and <c>admin</c> are one name.
/// </para>
/// <code>
/// public sealed class User : Authenticatable&lt;string&gt;
/// {
///     [SignInName] public Username Username { get; private set; }
/// }
///
/// await auth.SignIn(username, password);
/// </code>
/// <para>
/// Such an account has no address, so what needs one is refused rather than sent nowhere: a password-reset link, a
/// confirmation link, <c>AuthOptions.RequireConfirmedEmail</c> and passkeys. Without the attribute the account signs in
/// with <c>Email</c>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class SignInNameAttribute : Attribute;
