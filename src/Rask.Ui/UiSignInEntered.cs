namespace Rask;

/// <summary>What the sign-in card's form holds while it is filled in (<see cref="UiSignIn" />). Never kept or logged.</summary>
internal sealed class UiSignInEntered
{
    /// <summary>The address, or the name for an app that signs in by name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The password, emptied once it has been sent.</summary>
    public string Password { get; set; } = "";

    /// <summary>Whether the session outlives the browser session.</summary>
    public bool Remember { get; set; }
}
