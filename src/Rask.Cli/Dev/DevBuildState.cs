namespace Rask.Cli.Dev;

/// <summary>What <c>rask dev</c> currently believes about the app it is running.</summary>
internal enum DevBuildState
{
    /// <summary>The app built and is running (or is expected to be).</summary>
    Ok,

    /// <summary>A rebuild is in flight. The app may be momentarily down; that is not a failure.</summary>
    Building,

    /// <summary>The build failed. The app is down and will not come back until the code compiles.</summary>
    Failed,
}
