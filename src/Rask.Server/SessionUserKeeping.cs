namespace Rask.Server;

/// <summary>
///     How long a <see cref="SessionRevalidator{TUser}" />'s loaded users are kept, and the saves that drop them:
///     what <see cref="SessionRevalidatorBuilder" />'s steps say.
/// </summary>
/// <remarks>Written while the services are registered and only read after, so it needs no lock.</remarks>
internal sealed class SessionUserKeeping
{
    private readonly HashSet<Type> _forgetOnSave = [];

    /// <summary>How long a loaded user is kept. Zero, the default, keeps nothing: every event loads.</summary>
    public TimeSpan Keep { get; set; }

    /// <summary>Whether a save of <paramref name="aggregate" /> drops what is kept.</summary>
    public bool ForgetsOnSaveOf(Type aggregate) => _forgetOnSave.Contains(aggregate);

    /// <summary>Drops what is kept when an <paramref name="aggregate" /> is saved.</summary>
    public void ForgetOnSave(Type aggregate) => _forgetOnSave.Add(aggregate);
}
