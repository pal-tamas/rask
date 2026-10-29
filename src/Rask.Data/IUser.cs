namespace Rask.Data;

/// <summary>
///     Says which of the app's aggregates is its user: the row <c>Current.User</c> returns.
/// </summary>
/// <remarks>
///     <para>
///         Nothing to implement — the interface is the declaration, read by the build. Rask.Auth's
///         <c>Authenticatable</c> says it already, so an app that signs people in with it has <c>Current.User</c>
///         with no line of its own; one that keeps its users itself says it on its own aggregate:
///         <code>
/// public sealed class User : Aggregate&lt;Guid&gt;, IUser { … }
///         </code>
///     </para>
///     <para>
///         <c>Current.User</c> is then that user's read face (<c>UserRead?</c>), looked up by <c>Current.UserId</c>,
///         and <c>Current.RequiredUser</c> the same row or a thrown exception when nobody is signed in. A signed-in
///         user whose row is gone throws rather than reading as nobody.
///     </para>
///     <para>
///         One aggregate per app says it, keyed like the tenant: a <see cref="Guid" />, an <see cref="int" />, a
///         <see cref="long" /> or a <see cref="string" /> holding a <see cref="Guid" />.
///     </para>
/// </remarks>
public interface IUser;
